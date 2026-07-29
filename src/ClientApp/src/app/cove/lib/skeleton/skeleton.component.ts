import { Component, Input } from '@angular/core';

/**
 * A shimmering placeholder box, sized by inputs. Compose several into a card- or row-shaped
 * skeleton while real content loads.
 *
 * The shimmer is a moving gradient; the global `prefers-reduced-motion` guard (styles.scss) stops
 * it, leaving a static tinted box — still a valid placeholder.
 */
@Component({
  selector: 'cove-skeleton',
  standalone: true,
  template: `<span class="sk" aria-hidden="true"
    [style.width]="width" [style.height]="height" [style.borderRadius]="radius"></span>`,
  styles: [`
    .sk {
      display: block;
      background: linear-gradient(
        90deg,
        var(--surface-sunken) 25%,
        var(--surface-card-hover) 37%,
        var(--surface-sunken) 63%
      );
      background-size: 400% 100%;
      animation: sk-shimmer 1.4s ease infinite;
    }
    @keyframes sk-shimmer {
      from { background-position: 100% 0; }
      to   { background-position: 0 0; }
    }
  `],
})
export class SkeletonComponent {
  @Input() width = '100%';
  @Input() height = '16px';
  @Input() radius = 'var(--radius-sm)';
}
