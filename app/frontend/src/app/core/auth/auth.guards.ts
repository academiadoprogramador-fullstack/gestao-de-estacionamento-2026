import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

export const authenticatedGuard: CanActivateFn = async (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const session = await auth.ensureSession();
  return session
    ? true
    : router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};

export const roleGuard: CanActivateFn = async (route) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const session = await auth.ensureSession();
  if (!session) return router.parseUrl('/login');

  const requiredRoles = route.data['roles'] as string[] | undefined;
  return !requiredRoles?.length || requiredRoles.some((role) => session.roles.includes(role))
    ? true
    : router.parseUrl('/forbidden');
};
