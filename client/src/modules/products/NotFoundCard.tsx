import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router-dom'
import { isApiError } from '@/api/http'
import { Button } from '@/ui/Button'
import styles from './ProductForm.module.css'

/** A product (or its lookups) that could not be loaded: "not found" for a 404 or a bad id, otherwise the server's message. */
export function NotFoundCard({ error }: { error?: unknown }) {
  const { t } = useTranslation('products')
  const navigate = useNavigate()
  const message = isApiError(error) && error.status !== 404 ? error.message : t('Prod_NotFound', { ns: 'common' })

  return (
    <div className={styles.page}>
      <div className={styles.card} role="alert">
        <h1 className={styles.title}>{message}</h1>
        <div className={styles.actions}>
          <Button variant="primary" onClick={() => navigate('/products')}>
            {t('Prod_BackToProducts', { ns: 'common' })}
          </Button>
        </div>
      </div>
    </div>
  )
}
