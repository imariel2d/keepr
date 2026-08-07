import { Injectable, LOCALE_ID, inject } from '@angular/core';
import { DOCUMENT } from '@angular/common';
import { DEFAULT_LOCALE, isLocale, Locale, LOCALE_STORAGE_KEY, SUPPORTED_LOCALES } from './locale';

/**
 * Knows which locale this build is running as, and switches to another (#30). Because localization is
 * compile-time (@angular/localize), there is no in-place string swap: switching means recording the
 * choice and reloading into that locale's build (served under /{locale}/) — see
 * docs/feature-30-localization.md §2.1. The in-app path is preserved across the reload.
 */
@Injectable({ providedIn: 'root' })
export class LocaleService {
  private readonly doc = inject(DOCUMENT);

  /** The locale this build was compiled for (Angular sets LOCALE_ID per localized build). */
  readonly current: Locale = this.normalize(inject(LOCALE_ID));

  readonly supported = SUPPORTED_LOCALES;

  /**
   * Record `locale` as the preference and reload into its build, keeping the current in-app path and
   * query. A no-op when it's already the current locale. Callers that also persist the choice to the
   * account (the profile switcher) should do that first, then call this.
   */
  switchTo(locale: Locale): void {
    if (!isLocale(locale) || locale === this.current) return;
    this.remember(locale);
    this.doc.location.assign(`/${locale}/${this.pathWithinLocale()}`);
  }

  /** Persist the choice for the server root redirect (§4.3) and the next visit, without reloading. */
  remember(locale: Locale): void {
    // SameSite=Lax, one year. Read server-side only to pick the /{locale}/ redirect — never to
    // auto-set the account preference.
    this.doc.cookie = `${LOCALE_STORAGE_KEY}=${locale};path=/;max-age=31536000;samesite=lax`;
    try {
      this.doc.defaultView?.localStorage.setItem(LOCALE_STORAGE_KEY, locale);
    } catch {
      // localStorage can throw in private modes; the cookie is the source of truth anyway.
    }
  }

  /** The path after the /{locale} prefix, plus query + hash — what to re-open in the new build. */
  private pathWithinLocale(): string {
    const path = this.doc.location.pathname.replace(/^\/(en|es|fr)(?=\/|$)/, '').replace(/^\//, '');
    return path + this.doc.location.search + this.doc.location.hash;
  }

  private normalize(raw: string): Locale {
    const base = (raw || '').toLowerCase().split('-')[0];
    return isLocale(base) ? base : DEFAULT_LOCALE;
  }
}
