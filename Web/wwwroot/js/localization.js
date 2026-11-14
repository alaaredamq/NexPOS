/**
 * Localization Helper for Coduo POS
 * Manages loading and accessing translations from JSON files
 */

class Localization {
    constructor() {
        this.currentLanguage = this.detectLanguage();
        this.translations = {};
        this.isLoaded = false;
    }

    /**
     * Detect current language from window.__currentLanguage, URL, or HTML lang attribute
     */
    detectLanguage() {
        // First, try to get from window.__currentLanguage (injected by layout)
        if (window.__currentLanguage && (window.__currentLanguage === 'ar' || window.__currentLanguage === 'en')) {
            return window.__currentLanguage;
        }

        // Try to get language from URL path (/ar/ or /en/)
        const pathLang = window.location.pathname.match(/^\/([a-z]{2})\//);
        if (pathLang && (pathLang[1] === 'ar' || pathLang[1] === 'en')) {
            return pathLang[1];
        }

        // Try to get from HTML lang attribute
        const htmlLang = document.documentElement.lang;
        if (htmlLang && (htmlLang === 'ar' || htmlLang === 'en')) {
            return htmlLang;
        }

        // Default to English
        return 'en';
    }

    /**
     * Load translations from window.__translations (injected by layout) or fetch as fallback
     */
    async load() {
        if (this.isLoaded) {
            return this.translations;
        }

        // First, try to use translations injected by layout (window.__translations)
        if (window.__translations && typeof window.__translations === 'object') {
            this.translations = window.__translations;
            this.isLoaded = true;
            this.currentLanguage = window.__currentLanguage || this.detectLanguage();
            console.log(`Localization loaded from layout: ${this.currentLanguage}`);
            return this.translations;
        }

        // Fallback: fetch from JSON file if not injected (for edge cases)
        try {
            const response = await fetch(`/js/locales/${this.currentLanguage}.json`);
            if (!response.ok) {
                throw new Error(`Failed to load translations: ${response.status}`);
            }
            this.translations = await response.json();
            this.isLoaded = true;
            console.log(`Localization loaded from fetch: ${this.currentLanguage}`);
            return this.translations;
        } catch (error) {
            console.error('Error loading translations:', error);
            // Fallback to English
            if (this.currentLanguage !== 'en') {
                this.currentLanguage = 'en';
                return this.load();
            }
            // Return empty object if all fails
            this.translations = {};
            this.isLoaded = true;
            return this.translations;
        }
    }

    /**
     * Get translation by key path (e.g., "pos.cartIsEmpty")
     */
    t(keyPath, defaultValue = '') {
        if (!this.isLoaded) {
            console.warn('Translations not loaded yet. Call load() first.');
            return defaultValue || keyPath;
        }

        const keys = keyPath.split('.');
        let value = this.translations;

        for (const key of keys) {
            if (value && typeof value === 'object' && key in value) {
                value = value[key];
            } else {
                console.warn(`Translation key not found: ${keyPath}`);
                return defaultValue || keyPath;
            }
        }

        return value || defaultValue || keyPath;
    }

    /**
     * Get translation with placeholder replacement
     * Example: t('message', {name: 'John'}) where message is "Hello {name}"
     */
    tf(keyPath, params = {}, defaultValue = '') {
        let text = this.t(keyPath, defaultValue);
        
        // Replace placeholders
        Object.keys(params).forEach(key => {
            const regex = new RegExp(`\\{${key}\\}`, 'g');
            text = text.replace(regex, params[key]);
        });

        return text;
    }

    /**
     * Get current language code
     */
    getLanguage() {
        return this.currentLanguage;
    }

    /**
     * Check if current language is RTL
     */
    isRTL() {
        return this.currentLanguage === 'ar';
    }

    /**
     * Switch language and reload page
     * Preserves the current route structure: /{lang}/{controller}/{action}
     */
    switchLanguage(lang) {
        if (lang !== 'ar' && lang !== 'en') {
            console.error('Invalid language code. Use "ar" or "en".');
            return;
        }

        const currentPath = window.location.pathname;
        let newPath;

        // Check if path already has a language code (e.g., /en/POS or /ar/Products/Index)
        if (currentPath.match(/^\/(en|ar)\//)) {
            // Replace existing language code while preserving the rest of the path
            newPath = currentPath.replace(/^\/(en|ar)\//, `/${lang}/`);
        } else {
            // Add language code at the beginning
            // If path starts with /, add lang after it, otherwise add /lang/
            newPath = currentPath.startsWith('/') 
                ? `/${lang}${currentPath}` 
                : `/${lang}/${currentPath}`;
        }

        // Preserve query string and hash
        const queryString = window.location.search;
        const hash = window.location.hash;
        window.location.href = newPath + queryString + hash;
    }
}

// Create global instance
window.i18n = new Localization();

// Auto-load translations when DOM is ready
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', function() {
        window.i18n.load().then(() => {
            console.log('Translations ready');
            // Trigger custom event for other scripts
            window.dispatchEvent(new Event('localizationReady'));
        });
    });
} else {
    // DOM is already ready
    window.i18n.load().then(() => {
        console.log('Translations ready');
        window.dispatchEvent(new Event('localizationReady'));
    });
}

// Export for module usage
if (typeof module !== 'undefined' && module.exports) {
    module.exports = Localization;
}

