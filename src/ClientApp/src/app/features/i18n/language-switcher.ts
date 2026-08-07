import { ChangeDetectionStrategy, Component, EventEmitter, Output, inject } from '@angular/core';
import { LOCALE_NAMES, Locale } from '../../core/locale';
import { LocaleService } from '../../core/locale.service';

/**
 * A small, self-contained language picker (#30). Presentational: it shows the current locale and the
 * supported set (each named in its own language) and emits the chosen one — the host decides what to
 * do (the login screen just switches; the profile screen persists to the account first). A native
 * `<select>` so it's keyboard-operable and screen-reader-labelled for free.
 */
@Component({
  selector: 'app-language-switcher',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <select
      class="lang-select"
      [value]="locale.current"
      (change)="onChange($event)"
      aria-label="Language"
      i18n-aria-label="@@lang.label">
      @for (l of locale.supported; track l) {
        <option [value]="l">{{ names[l] }}</option>
      }
    </select>
  `,
  styles: [
    `
      .lang-select {
        min-height: var(--control-height-sm, 2rem);
        padding: 0 var(--space-2, 0.5rem);
        color: var(--text-1, inherit);
        background: var(--surface-1, transparent);
        border: 1px solid var(--border-1, currentColor);
        border-radius: var(--radius-md, 0.5rem);
        font: inherit;
        cursor: pointer;
      }
      .lang-select:focus-visible {
        outline: var(--focus-ring, 2px solid);
        outline-offset: 2px;
      }
    `,
  ],
})
export class LanguageSwitcher {
  protected readonly locale = inject(LocaleService);
  protected readonly names = LOCALE_NAMES;

  /** The locale the user picked. The host wires this (switch, and persist if signed in). */
  @Output() readonly localeSelect = new EventEmitter<Locale>();

  protected onChange(event: Event): void {
    this.localeSelect.emit((event.target as HTMLSelectElement).value as Locale);
  }
}
