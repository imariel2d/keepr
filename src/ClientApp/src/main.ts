import { registerLocaleData } from '@angular/common';
import localeEs from '@angular/common/locales/es';
import localeFr from '@angular/common/locales/fr';
import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { App } from './app/app';

// Locale data for the non-source builds (#30) so CommonModule's number/date pipes format correctly
// in es/fr. LOCALE_ID itself is set per localized build by @angular/localize; en is built in.
registerLocaleData(localeEs);
registerLocaleData(localeFr);

bootstrapApplication(App, appConfig)
  .catch((err) => console.error(err));
