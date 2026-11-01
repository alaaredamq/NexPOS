import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import '@/core/i18n'
import { loadNamespaces, setLanguage } from '@/core/i18n'
import { ALL_PERMISSIONS, Permissions } from '@/api/permissions'
import { makeSession } from '@/test/sessionFixture'
import { toast } from '@/ui/toast'
import accountRoutes from '../account/routes'
import { fakeApi, json, mountModule, sessionWith, stubMatchMedia, type FakeApi } from '../_shared/testApp'
import routes from './routes'

const user = (id: number, username: string, overrides = {}) => ({
  id,
  username,
  email: `${username}@example.com`,
  firstName: username,
  lastName: 'Tester',
  fullName: `${username} Tester`,
  roleId: 4,
  roleName: 'Cashier',
  isActive: true,
  mustChangePin: false,
  lastLoginAt: null,
  createdAt: '2026-01-05T10:00:00Z',
  ...overrides,
})

const role = (id: number, name: string, overrides = {}) => ({
  id,
  name,
  description: `${name} role`,
  isSystemRole: false,
  isSuperAdmin: false,
  userCount: 1,
  permissionCount: 1,
  createdAt: '2026-01-05T10:00:00Z',
  ...overrides,
})

const page = <T,>(items: T[]) => ({ items, total: items.length, page: 1, pageSize: 25 })

let api: FakeApi

beforeAll(async () => {
  stubMatchMedia()
  await loadNamespaces('common', 'users', 'account')
})

beforeEach(async () => {
  localStorage.clear()
  toast.clear()
  await setLanguage('en')
})
afterEach(() => vi.unstubAllGlobals())

const ADMIN = [Permissions.Users_View, Permissions.Users_Create, Permissions.Users_Edit, Permissions.Users_Delete, Permissions.Users_Manage]

function open(path: string, ...permissions: string[]) {
  const session = sessionWith(...permissions)
  api = fakeApi({ ...session, user: { ...session.user, id: 1 } })
  api.routes['GET /api/v1/users'] = () => json(page([user(1, 'layla', { roleName: 'Admin', roleId: 2 }), user(2, 'sara', { mustChangePin: true }), user(3, 'omar', { isActive: false })]))
  api.routes['GET /api/v1/users/summary'] = () => json({ totalUsers: 3, totalRoles: 4, activeUsers: 2, adminUsers: 1 })
  api.routes['GET /api/v1/roles'] = () => json(page([role(2, 'Admin'), role(4, 'Cashier'), role(1, 'SuperAdmin', { isSystemRole: true, isSuperAdmin: true, permissionCount: ALL_PERMISSIONS.length })]))
  return mountModule(path, routes)
}

describe('users list', () => {
  it('shows the rows, the badges and the tiles', async () => {
    open('/users', ...ADMIN)

    expect(await screen.findByText('sara Tester')).toBeInTheDocument()
    expect(screen.getByText('Must change PIN')).toBeInTheDocument()
    expect(screen.getByText('Inactive')).toBeInTheDocument()
    expect(await screen.findByText('Total Users')).toBeInTheDocument()
  })

  it('does not offer to disable or delete yourself, but does for the others', async () => {
    open('/users', ...ADMIN)
    await screen.findByText('sara Tester')

    // 3 rows: Edit on each, Disable/Enable and Delete on the two that are not the signed-in user (id 1).
    expect(screen.getAllByRole('button', { name: 'Edit' })).toHaveLength(3)
    expect(screen.getAllByRole('button', { name: 'Delete' })).toHaveLength(2)
    expect(screen.getAllByRole('button', { name: /^(Disable|Enable)$/ })).toHaveLength(2)
  })

  it('a user who may only view sees no buttons to change anything', async () => {
    open('/users', Permissions.Users_View)
    await screen.findByText('sara Tester')

    expect(screen.queryByRole('button', { name: 'Edit' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Delete' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Add User' })).not.toBeInTheDocument()
  })

  it('without Users.View the route bounces to the forbidden page', async () => {
    const router = open('/users', Permissions.Orders_View)

    await waitFor(() => expect(router.state.location.pathname).toBe('/forbidden'))
  })

  it('disabling a user calls the active endpoint with the flag', async () => {
    open('/users', ...ADMIN)
    api.routes['PUT /api/v1/users/2/active'] = () => json(user(2, 'sara', { isActive: false }))
    await screen.findByText('sara Tester')

    const row = screen.getByText('sara Tester').closest('tr')!
    fireEvent.click(within(row).getByRole('button', { name: 'Disable' }))

    await waitFor(() => expect(api.calls.find((c) => c.method === 'PUT')?.body).toEqual({ isActive: false }))
    expect(await screen.findByText('User disabled.')).toBeInTheDocument()
  })

  it('deleting asks first and shows the server reason inside the dialog when the user has history', async () => {
    open('/users', ...ADMIN)
    api.routes['DELETE /api/v1/users/2'] = () =>
      new Response(JSON.stringify({ status: 409, code: 'user_in_use', title: 'In use', detail: 'This user has activity on record.' }), {
        status: 409,
        headers: { 'Content-Type': 'application/problem+json' },
      })
    await screen.findByText('sara Tester')

    const row = screen.getByText('sara Tester').closest('tr')!
    fireEvent.click(within(row).getByRole('button', { name: 'Delete' }))
    const dialog = await screen.findByRole('dialog')
    fireEvent.click(within(dialog).getByRole('button', { name: 'Delete' }))

    expect(await within(dialog).findByText('This user has activity on record.')).toBeInTheDocument()
    // The confirm dialog lives in a module-level store: close it so it does not leak into the next test.
    fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })
})

describe('create and edit a user', () => {
  it('refuses a missing role, a bad email and a 3-digit PIN without calling the API', async () => {
    open('/users', ...ADMIN)
    await screen.findByText('sara Tester')

    fireEvent.click(screen.getAllByRole('button', { name: 'Add User' })[0]!)
    const dialog = await screen.findByRole('dialog', { name: 'Add User' })
    fireEvent.change(within(dialog).getByLabelText(/^Username/), { target: { value: 'newbie' } })
    fireEvent.change(within(dialog).getByLabelText(/Email Address/), { target: { value: 'nope' } })
    fireEvent.change(within(dialog).getByLabelText(/Login PIN/), { target: { value: '123' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Create User' }))

    expect(await within(dialog).findByText('Enter a valid email address.')).toBeInTheDocument()
    expect(within(dialog).getByText('The PIN must be exactly 4 digits.')).toBeInTheDocument()
    expect(within(dialog).getByText('Choose a role.')).toBeInTheDocument()
    expect(api.calls.some((c) => c.method === 'POST')).toBe(false)
  })

  it('saves an edit with trimmed text and the unchanged role, and lands the server error of a taken email on its field', async () => {
    open('/users', ...ADMIN)
    api.routes['PUT /api/v1/users/2'] = () =>
      new Response(JSON.stringify({ status: 400, code: 'validation_failed', title: 'Invalid', errors: { email: ['That email address is already in use.'] } }), {
        status: 400,
        headers: { 'Content-Type': 'application/problem+json' },
      })
    await screen.findByText('sara Tester')

    const row = screen.getByText('sara Tester').closest('tr')!
    fireEvent.click(within(row).getAllByRole('button', { name: 'Edit' })[0]!)
    const dialog = await screen.findByRole('dialog', { name: 'Edit User' })
    expect(within(dialog).getByLabelText(/^Username/)).toBeDisabled()
    fireEvent.change(within(dialog).getByLabelText(/Email Address/), { target: { value: '  taken@example.com ' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save' }))

    expect(await within(dialog).findByText('That email address is already in use.')).toBeInTheDocument()
    expect(api.calls.find((c) => c.method === 'PUT')?.body).toEqual({ email: 'taken@example.com', firstName: 'sara', lastName: 'Tester', roleId: 4, isActive: true })
    expect(screen.getByRole('dialog', { name: 'Edit User' })).toBeInTheDocument()
  })

  it('reset PIN only accepts four digits and posts them', async () => {
    open('/users', ...ADMIN)
    api.routes['POST /api/v1/users/2/reset-pin'] = () => new Response(null, { status: 204 })
    await screen.findByText('sara Tester')

    const row = screen.getByText('sara Tester').closest('tr')!
    fireEvent.click(within(row).getAllByRole('button', { name: 'Edit' })[0]!)
    fireEvent.click(await screen.findByRole('button', { name: 'Reset PIN' }))
    const dialog = await screen.findByRole('dialog', { name: 'Reset PIN' })
    const input = within(dialog).getByLabelText(/New PIN/)
    const submit = within(dialog).getByRole('button', { name: 'Reset PIN' })

    fireEvent.change(input, { target: { value: '12a4b' } })
    expect(input).toHaveValue('124')
    expect(submit).toBeDisabled()
    fireEvent.change(input, { target: { value: '1243' } })
    fireEvent.click(submit)

    await waitFor(() => expect(api.calls.find((c) => c.method === 'POST')?.body).toEqual({ newPin: '1243' }))
  })
})

describe('roles and the permission matrix', () => {
  const cashierDetails = { role: role(4, 'Cashier'), permissions: [Permissions.Orders_View] }

  it('lists the roles and locks SuperAdmin against deleting', async () => {
    open('/users/roles', ...ADMIN)

    expect(await screen.findByText('SuperAdmin')).toBeInTheDocument()
    expect(screen.getAllByText('System').length).toBeGreaterThan(0)
    // Admin and Cashier can be deleted (the server refuses while they are in use); the system role cannot.
    expect(screen.getAllByRole('button', { name: 'Delete' })).toHaveLength(2)
  })

  it('a group box ticks all of its permissions, and Save sends the complete tick state', async () => {
    open('/users/roles', ...ADMIN)
    api.routes['GET /api/v1/roles/4'] = () => json(cashierDetails)
    api.routes['PUT /api/v1/roles/4'] = () => json(cashierDetails)
    await screen.findByText('SuperAdmin')

    const row = screen.getByText('Cashier role').closest('tr')!
    fireEvent.click(within(row).getByRole('button', { name: 'Edit' }))
    const dialog = await screen.findByRole('dialog', { name: 'Edit Role' })
    await within(dialog).findByLabelText(/Role Name/)
    expect(within(dialog).getByText('1 / ' + ALL_PERMISSIONS.length + ' selected')).toBeInTheDocument()

    const products = ALL_PERMISSIONS.filter((p) => p.startsWith('Products.'))
    fireEvent.click(within(dialog).getByRole('checkbox', { name: 'Products' }))
    expect(within(dialog).getByText(`${1 + products.length} / ${ALL_PERMISSIONS.length} selected`)).toBeInTheDocument()

    fireEvent.click(within(dialog).getByRole('button', { name: 'Save Role' }))

    await waitFor(() => expect(api.calls.find((c) => c.method === 'PUT')).toBeTruthy())
    const sent = (api.calls.find((c) => c.method === 'PUT')!.body as { permissions: string[]; name: string })
    expect(sent.name).toBe('Cashier')
    expect([...sent.permissions].sort()).toEqual([Permissions.Orders_View, ...products].sort())
  })

  it('SuperAdmin opens read-only with every box ticked and no Save button', async () => {
    open('/users/roles', ...ADMIN)
    api.routes['GET /api/v1/roles/1'] = () => json({ role: role(1, 'SuperAdmin', { isSystemRole: true, isSuperAdmin: true }), permissions: [...ALL_PERMISSIONS] })
    await screen.findByText('SuperAdmin')

    const row = screen.getAllByText('SuperAdmin')[0]!.closest('tr')!
    fireEvent.click(within(row).getByRole('button', { name: 'View Role' }))
    const dialog = await screen.findByRole('dialog', { name: 'View Role' })

    expect(await within(dialog).findByText(/always holds every permission/)).toBeInTheDocument()
    expect(within(dialog).queryByRole('button', { name: 'Save Role' })).not.toBeInTheDocument()
    expect(within(dialog).getByRole('checkbox', { name: 'Products' })).toBeDisabled()
  })

  it('shows the server name error under the name field', async () => {
    open('/users/roles', ...ADMIN)
    api.routes['POST /api/v1/roles'] = () =>
      new Response(JSON.stringify({ status: 400, code: 'validation_failed', title: 'Invalid', errors: { name: ['A role with that name already exists.'] } }), {
        status: 400,
        headers: { 'Content-Type': 'application/problem+json' },
      })
    await screen.findByText('SuperAdmin')

    fireEvent.click(screen.getAllByRole('button', { name: 'Add Role' })[0]!)
    const dialog = await screen.findByRole('dialog', { name: 'Add Role' })
    fireEvent.change(within(dialog).getByLabelText(/Role Name/), { target: { value: 'Cashier' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save Role' }))

    expect(await within(dialog).findByText('A role with that name already exists.')).toBeInTheDocument()
  })

  it('without Users.Manage the roles are listed but cannot be added, edited or deleted', async () => {
    open('/users/roles', Permissions.Users_View)

    expect(await screen.findByText('SuperAdmin')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Add Role' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Edit' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Delete' })).not.toBeInTheDocument()
  })
})

describe('change my PIN', () => {
  const press = (digits: string) => {
    for (const d of digits) fireEvent.click(screen.getByRole('button', { name: d }))
  }

  function openAccount(path: string, mustChangePin = false) {
    const session = makeSession({ permissions: [] })
    api = fakeApi({ ...session, user: { ...session.user, mustChangePin } })
    api.routes['GET /api/v1/account/profile'] = () =>
      json({ id: 2, username: 'sara', email: 'sara@example.com', firstName: 'Sara', lastName: 'Cashier', fullName: 'Sara Cashier', roleId: 4, roleName: 'Cashier', lastLoginAt: null, mustChangePin })
    return mountModule(path, accountRoutes)
  }

  it('the profile page shows the account and links to the PIN screen', async () => {
    openAccount('/account/profile')

    expect(await screen.findByText('sara@example.com')).toBeInTheDocument()
    expect(screen.getByText('Never')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Change PIN' })).toHaveAttribute('href', '/account/change-pin')
  })

  it('collects the three PINs on one keypad, moving on when a field is full, and posts them', async () => {
    const router = openAccount('/account/change-pin')
    api.routes['POST /api/v1/account/change-pin'] = () => new Response(null, { status: 204 })
    await screen.findByRole('radio', { name: /Current PIN/ })

    press('1111')
    expect(screen.getByRole('radio', { name: /^New PIN/ })).toHaveAttribute('aria-checked', 'true')
    press('2222')
    press('2222')
    fireEvent.click(screen.getByRole('button', { name: 'Update PIN' }))

    await waitFor(() => expect(api.calls.find((c) => c.method === 'POST')?.body).toEqual({ currentPin: '1111', newPin: '2222', confirmPin: '2222' }))
    await waitFor(() => expect(router.state.location.pathname).toBe('/account/profile'))
  })

  it('refuses a confirmation that differs, without calling the API', async () => {
    openAccount('/account/change-pin')
    await screen.findByRole('radio', { name: /Current PIN/ })

    press('1111')
    press('2222')
    press('3333')
    fireEvent.click(screen.getByRole('button', { name: 'Update PIN' }))

    expect(await screen.findByText('PINs do not match')).toBeInTheDocument()
    expect(api.calls.some((c) => c.method === 'POST')).toBe(false)
  })

  it('shows a wrong current PIN from the server on that field and clears it', async () => {
    openAccount('/account/change-pin')
    api.routes['POST /api/v1/account/change-pin'] = () =>
      new Response(JSON.stringify({ status: 400, code: 'validation_failed', title: 'Invalid', errors: { currentPin: ['Current PIN is incorrect'] } }), {
        status: 400,
        headers: { 'Content-Type': 'application/problem+json' },
      })
    await screen.findByRole('radio', { name: /Current PIN/ })

    press('9999')
    press('2222')
    press('2222')
    fireEvent.click(screen.getByRole('button', { name: 'Update PIN' }))

    expect(await screen.findByText('Current PIN is incorrect')).toBeInTheDocument()
    expect(screen.getByRole('radio', { name: /Current PIN/ })).toHaveAttribute('aria-checked', 'true')
  })

  it('after an admin set the PIN, every page leads here and the current PIN is not asked for', async () => {
    const router = openAccount('/account/profile', true)

    await waitFor(() => expect(router.state.location.pathname).toBe('/account/change-pin'))
    expect(await screen.findByText(/An administrator set your PIN/)).toBeInTheDocument()
    expect(screen.queryByRole('radio', { name: /Current PIN/ })).not.toBeInTheDocument()
    expect(screen.getAllByRole('radio')).toHaveLength(2)
    expect(screen.queryByRole('button', { name: 'Cancel' })).not.toBeInTheDocument()
  })
})

