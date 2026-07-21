import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'cove-avatar',
  standalone: true,
  imports: [CommonModule],
  template: `
    <img *ngIf="src" [src]="src" [alt]="name"
         [ngStyle]="{ width: size + 'px', height: size + 'px', borderRadius: '50%', objectFit: 'cover' }" />
    <div *ngIf="!src" [ngStyle]="boxStyle()">{{ initials }}</div>`,
})
export class AvatarComponent {
  @Input() name = '?';
  @Input() src?: string;
  @Input() size = 32;
  private palette = ['var(--brand-500)', 'var(--teal-500)', 'var(--amber-600)', 'var(--brand-700)', 'var(--teal-700)'];
  get initials() {
    return (this.name || '?').trim().split(/\s+/).slice(0, 2).map(w => w[0]?.toUpperCase()).join('');
  }
  get bg() {
    const hash = (this.name || '').split('').reduce((a, c) => a + c.charCodeAt(0), 0);
    return this.palette[hash % this.palette.length];
  }
  boxStyle() {
    return {
      width: this.size + 'px', height: this.size + 'px', borderRadius: '50%', background: this.bg,
      color: '#fff', display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
      fontFamily: 'var(--font-body)', fontWeight: 700, fontSize: Math.round(this.size * 0.4) + 'px',
    };
  }
}
