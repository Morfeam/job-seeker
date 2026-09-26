import { Component, OnDestroy, OnInit } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { Subscription } from 'rxjs';

// Bump this whenever the terms content changes materially. The sign-up form sends it to
// the backend, which records it alongside the acceptance timestamp on the customer row.
export const TERMS_VERSION = '2026-09-26';

interface TermsSection {
  id: string;
  title: string;
}

@Component({
  selector: 'app-terms',
  standalone: false,
  templateUrl: './terms.html',
  styleUrl: './terms.css',
})
export class Terms implements OnInit, OnDestroy {
  readonly effectiveDate = 'September 26, 2026';
  readonly version = TERMS_VERSION;
  readonly contactEmail = 'joe.prints26@gmail.com';
  // TODO: replace [STATE] with the governing US state before launch.
  readonly governingLaw = 'the State of [STATE], United States';

  // Each id doubles as the URL fragment, e.g. /terms-of-service#privacy.
  readonly sections: TermsSection[] = [
    { id: 'disclaimer', title: 'Job Scraping Disclaimer' },
    { id: 'terms', title: 'Terms of Service' },
    { id: 'privacy', title: 'Privacy Policy' },
    { id: 'ai', title: 'AI & Automated Processing Disclosure' },
    { id: 'contact', title: 'Contact' },
  ];

  private open = new Set<string>(this.sections.map((s) => s.id));
  private fragmentSub?: Subscription;

  constructor(private route: ActivatedRoute) {}

  ngOnInit(): void {
    // A deep link opens only the linked section; no fragment shows everything expanded.
    this.fragmentSub = this.route.fragment.subscribe((fragment) => {
      if (fragment && this.sections.some((s) => s.id === fragment)) this.focusSection(fragment);
    });
  }

  ngOnDestroy(): void {
    this.fragmentSub?.unsubscribe();
  }

  // Also bound to the contents links directly, since re-clicking the current fragment doesn't re-emit.
  focusSection(id: string): void {
    this.open = new Set([id]);
    setTimeout(() => this.scrollTo(id));
  }

  isOpen(id: string): boolean {
    return this.open.has(id);
  }

  toggle(id: string): void {
    if (this.open.has(id)) this.open.delete(id);
    else this.open.add(id);
  }

  expandAll(): void {
    this.open = new Set(this.sections.map((s) => s.id));
  }

  collapseAll(): void {
    this.open.clear();
  }

  scrollTo(id: string): void {
    document.getElementById(id)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }
}
