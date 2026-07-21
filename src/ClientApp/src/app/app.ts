import { Component, inject } from '@angular/core';
import { Router, RouterOutlet } from '@angular/router';
import { AuthService } from './core/auth.service';
import { ThemeService } from './core/theme.service';
import { ButtonComponent } from './cove/lib/button/button.component';
import { IconComponent } from './cove/lib/icon/icon.component';
import { IconButtonComponent } from './cove/lib/icon-button/icon-button.component';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, ButtonComponent, IconComponent, IconButtonComponent],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  protected readonly auth = inject(AuthService);
  protected readonly theme = inject(ThemeService);
  private readonly router = inject(Router);

  logout(): void {
    this.auth.logout();
    this.router.navigate(['/login']);
  }
}
