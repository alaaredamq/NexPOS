import i18next, { type i18n } from 'i18next'
import { initReactI18next } from 'react-i18next'

/**
 * A tiny i18n instance so `_kit` and the Vitest suites can render `ui/` in English and Arabic without the
 * real pipeline (0E). The keys here are the `common.*` keys that `ui/` components ask for — 0E seeds the
 * same keys into `locales/{en,ar}/common.json`, after which this file is only used by tests.
 */
export const KIT_RESOURCES = {
  en: {
    translation: {
      common: {
        loading: 'Loading', cancel: 'Cancel', close: 'Close', dismiss: 'Dismiss', working: 'Working…', actionFailed: 'Action failed',
        search: 'Search', clearSearch: 'Clear search', clearSelection: 'Clear selection', noMatches: 'No matches', loadFailed: 'Could not load options',
        pagination: 'Pagination', pageRange: '{{from}}–{{to}} of {{total}}', previousPage: 'Previous page', nextPage: 'Next page', goToPage: 'Page {{page}}', rowsPerPage: 'Rows',
        columns: 'Columns', actions: 'Actions', retry: 'Try again', noResults: 'Nothing here yet',
        managerPinTitle: 'Manager PIN', managerPinSubtitle: 'Required to {{action}}', managerPinError: 'Incorrect PIN',
        pinEntered: '{{count}} of {{total}} digits entered', clear: 'Clear', backspace: 'Backspace',
        scannerReady: 'SCANNER READY', scanReady: 'SCAN READY', decreaseQty: 'Decrease quantity', increaseQty: 'Increase quantity',
        dateRange: 'Date range', from: 'From', to: 'To', today: 'Today', last7Days: '7 days', last30Days: '30 days', thisMonth: 'This month',
      },
    },
  },
  ar: {
    translation: {
      common: {
        loading: 'جارٍ التحميل', cancel: 'إلغاء', close: 'إغلاق', dismiss: 'إخفاء', working: 'جارٍ التنفيذ…', actionFailed: 'فشلت العملية',
        search: 'بحث', clearSearch: 'مسح البحث', clearSelection: 'مسح الاختيار', noMatches: 'لا نتائج', loadFailed: 'تعذّر تحميل الخيارات',
        pagination: 'التنقل بين الصفحات', pageRange: '{{from}}–{{to}} من {{total}}', previousPage: 'الصفحة السابقة', nextPage: 'الصفحة التالية', goToPage: 'الصفحة {{page}}', rowsPerPage: 'الصفوف',
        columns: 'الأعمدة', actions: 'إجراءات', retry: 'حاول مجدداً', noResults: 'لا يوجد شيء بعد',
        managerPinTitle: 'رمز المدير', managerPinSubtitle: 'مطلوب من أجل {{action}}', managerPinError: 'الرمز غير صحيح',
        pinEntered: 'تم إدخال {{count}} من {{total}} أرقام', clear: 'مسح', backspace: 'حذف',
        scannerReady: 'الماسح جاهز', scanReady: 'جاهز للمسح', decreaseQty: 'تقليل الكمية', increaseQty: 'زيادة الكمية',
        dateRange: 'نطاق التاريخ', from: 'من', to: 'إلى', today: 'اليوم', last7Days: '7 أيام', last30Days: '30 يوماً', thisMonth: 'هذا الشهر',
      },
    },
  },
} as const

export type KitLang = 'en' | 'ar'

let instance: i18n | null = null

/** Idempotent. Returns a dedicated instance (does not touch the global i18next that 0E will configure). */
export function getKitI18n(lang: KitLang = 'en'): i18n {
  if (!instance) {
    instance = i18next.createInstance()
    void instance.use(initReactI18next).init({
      resources: KIT_RESOURCES,
      lng: lang,
      fallbackLng: 'en',
      interpolation: { escapeValue: false },
      initAsync: false,
    })
  }
  return instance
}

/** Flips the document the way the real language toggle will (D8): `<html lang dir>`. */
export function applyDocumentLang(lang: KitLang) {
  document.documentElement.lang = lang
  document.documentElement.dir = lang === 'ar' ? 'rtl' : 'ltr'
}
