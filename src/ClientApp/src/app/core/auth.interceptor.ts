import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthService } from './auth.service';

/**
 * Reacts to the session dying.
 *
 * Nothing is attached on the way out any more — the session is a cookie the browser sends by
 * itself, and only to this origin. That also means presigned storage PUTs (cross-origin, raw
 * fetch in UploadService) cannot accidentally carry credentials, which the previous bearer-token
 * version had to avoid by hand.
 *
 * On the way back, a 401 means the cookie is gone, expired, or revoked. Clearing local state and
 * routing to /login is what stops an expired session from leaving a dead UI on screen firing
 * failing calls — the app previously had no 401 handling at all.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  return next(req).pipe(
    catchError((err: unknown) => {
      // Only our own API: a 401 from anywhere else says nothing about our session. The session
      // probe is excluded because "not signed in" is its ordinary answer, and redirecting on it
      // would fight the guard that asked the question.
      const isOurApi = req.url.startsWith('/api');
      const isProbe = req.url === '/api/auth/session';

      if (err instanceof HttpErrorResponse && err.status === 401 && isOurApi && !isProbe) {
        auth.clear();
        void router.navigate(['/login']);
      }
      return throwError(() => err);
    })
  );
};
