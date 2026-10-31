// `npm run api:types` (REACT_MIGRATION.md D6, 0A): regenerates the API contract types, with no server running.
//
//   1. runs `Web.exe --dump-api-contract <tmp>` (the app builds its OpenAPI document and exits before touching a database),
//   2. turns swagger.json into src/api/schema.d.ts with openapi-typescript,
//   3. copies the permission keys to src/api/permissions.ts.
//
// Both outputs are committed. Never hand-merge them: on a conflict take either side and run this again.
// Flags:  --no-build  reuse the last build of Web (faster when only the client changed)
import { spawnSync } from 'node:child_process'
import { copyFileSync, mkdirSync, mkdtempSync, rmSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const client = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const web = resolve(client, '..', 'Web')
const apiDir = join(client, 'src', 'api')
const noBuild = process.argv.includes('--no-build')

const work = mkdtempSync(join(tmpdir(), 'nexpos-contract-'))
const run = (command, args, options = {}) => {
  const result = spawnSync(command, args, { stdio: 'inherit', ...options })
  if (result.status !== 0) {
    console.error(`\napi:types failed: ${command} ${args.join(' ')} (exit ${result.status ?? result.error})`)
    rmSync(work, { recursive: true, force: true })
    process.exit(result.status ?? 1)
  }
}

try {
  // The dump needs a JWT secret to start up and must not touch the real database or log folder.
  const env = {
    ...process.env,
    Jwt__SecretKey: 'contract-dump-only-not-a-secret-0123456789',
    NEXPOS_DB_PATH: join(work, 'contract.db'),
    NEXPOS_LOG_DIR: join(work, 'logs'),
    ASPNETCORE_ENVIRONMENT: 'Development',
  }
  run(
    'dotnet',
    ['run', '--project', 'Web.csproj', '--no-launch-profile', '-v', 'q', ...(noBuild ? ['--no-build'] : []), '--', '--dump-api-contract', work],
    { cwd: web, env },
  )

  mkdirSync(apiDir, { recursive: true })
  run(process.execPath, [join(client, 'node_modules', 'openapi-typescript', 'bin', 'cli.js'), join(work, 'swagger.json'), '-o', join(apiDir, 'schema.d.ts')], { cwd: client })
  copyFileSync(join(work, 'permissions.ts'), join(apiDir, 'permissions.ts'))
  console.log('\napi:types: updated src/api/schema.d.ts and src/api/permissions.ts')
} finally {
  rmSync(work, { recursive: true, force: true })
}
