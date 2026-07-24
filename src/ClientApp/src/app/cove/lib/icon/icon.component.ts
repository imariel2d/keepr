import { Component, ElementRef, Input, OnChanges } from '@angular/core';

declare const lucide: any;

/** Renders a Lucide icon by name. Requires lucide loaded globally (CDN or npm). */
@Component({
  selector: 'cove-icon',
  standalone: true,
  template: '',
})
export class IconComponent implements OnChanges {
  @Input() name!: string;
  @Input() size = 18;
  @Input() color?: string;

  constructor(private el: ElementRef<HTMLElement>) {}

  // Re-render from a fresh placeholder on every change. lucide.createIcons() *replaces* the
  // <i data-lucide> node with a brand-new <svg>, so we cannot bind attributes on it and expect
  // later changes to take: Angular would keep updating the detached <i> while the live <svg>
  // stayed frozen at its first value. That is exactly what made a dynamic [name] (theme toggle,
  // the register checklist ticks) never update. Rewriting innerHTML each time makes the svg match.
  ngOnChanges(): void {
    if (typeof lucide === 'undefined') return;

    const host = this.el.nativeElement;
    host.replaceChildren();

    const placeholder = document.createElement('i');
    placeholder.setAttribute('data-lucide', this.name);
    Object.assign(placeholder.style, {
      width: `${this.size}px`,
      height: `${this.size}px`,
      display: 'inline-flex',
      flexShrink: '0',
      color: this.color || 'currentColor',
    });
    host.appendChild(placeholder);

    lucide.createIcons({ nameAttr: 'data-lucide', root: host });
  }
}
