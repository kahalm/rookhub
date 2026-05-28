import { HttpInterceptorFn } from '@angular/common/http';
import { catchError, switchMap, throwError, timer } from 'rxjs';

export const retryInterceptor: HttpInterceptorFn = (req, next) => {
  return next(req).pipe(
    catchError(err => {
      const retryable = err.status === 502 || err.status === 503 || err.status === 0;
      if (retryable && !req.headers.has('X-Retry')) {
        const retryReq = req.clone({ setHeaders: { 'X-Retry': '1' } });
        return timer(1000).pipe(switchMap(() => next(retryReq)));
      }
      return throwError(() => err);
    })
  );
};
