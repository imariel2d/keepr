// The set of UI locales the app is localized into (#30). Compile-time i18n via @angular/localize:
// each locale is its own build served under /en//es//fr/. Mirrors the server's SupportedLanguages —
// the two lists must stay in step (the i18n-translations skill). See docs/feature-30-localization.md.

export const SUPPORTED_LOCALES = ['en', 'es', 'fr'] as const;
export type Locale = (typeof SUPPORTED_LOCALES)[number];

/** The locale served when a preference is unset. Also the i18n source locale. */
export const DEFAULT_LOCALE: Locale = 'en';

/** Each language's name in its own language — a proper noun, so never itself translated. */
export const LOCALE_NAMES: Record<Locale, string> = {
  en: 'English',
  es: 'Español',
  fr: 'Français',
};

/** Cookie/localStorage key the client sets on an explicit choice and the server reads for the root
 *  redirect (§3.2 / §4.3). */
export const LOCALE_STORAGE_KEY = 'keepr_lang';

/** True when `value` is a supported locale code (already normalized — lowercase, trimmed). */
export function isLocale(value: string): value is Locale {
  return (SUPPORTED_LOCALES as readonly string[]).includes(value);
}
