import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';

type Tone = 'neutral' | 'accent' | 'success' | 'warning' | 'danger';

@Component({
  selector: 'cove-badge',
  standalone: true,
  imports: [CommonModule],
  template: `<span [ngStyle]="style()"><ng-content></ng-content></span>`,
})
export class BadgeComponent {
  @Input() tone: Tone = 'neutral';
  private tones: Record<Tone, any> = {
    neutral: { background: 'var(--surface-sunken)', color: 'var(--text-secondary)' },
    accent: { background: 'var(--accent-subtle)', color: 'var(--accent-subtle-text)' },
    success: { background: 'var(--teal-50)', color: 'var(--teal-700)' },
    warning: { background: '#FBF1DC', color: 'var(--amber-600)' },
    danger: { background: 'var(--danger-subtle)', color: 'var(--danger)' },
  };
  style() {
    return {
      display: 'inline-flex', alignItems: 'center', height: '22px', padding: '0 9px',
      borderRadius: 'var(--radius-full)', fontFamily: 'var(--font-body)', fontWeight: 600,
      fontSize: '12px', ...this.tones[this.tone],
    };
  }
}
