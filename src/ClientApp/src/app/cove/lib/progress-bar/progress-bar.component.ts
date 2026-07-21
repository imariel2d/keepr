import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';

type Tone = 'accent' | 'success' | 'warning' | 'danger';

@Component({
  selector: 'cove-progress-bar',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div [ngStyle]="{ width: '100%', height: height + 'px', borderRadius: 'var(--radius-full)', background: 'var(--surface-sunken)', overflow: 'hidden' }">
      <div [ngStyle]="fillStyle()"></div>
    </div>`,
})
export class ProgressBarComponent {
  @Input() value = 0;
  @Input() tone: Tone = 'accent';
  @Input() height = 6;
  private colors: Record<Tone, string> = { accent: 'var(--accent)', success: 'var(--success)', warning: 'var(--warning)', danger: 'var(--danger)' };
  fillStyle() {
    return {
      width: Math.max(0, Math.min(100, this.value)) + '%', height: '100%',
      background: this.colors[this.tone], borderRadius: 'var(--radius-full)',
      transition: 'width var(--duration-slow) var(--ease-standard)',
    };
  }
}
