import { Component, HostListener } from '@angular/core';
import { Observable } from 'rxjs';
import { AuthService } from '../auth';

@Component({
  selector: 'app-navbar',
  standalone: false,
  templateUrl: './navbar.html',
  styleUrl: './navbar.css',
})
export class Navbar {
  isLoggedIn: Observable<boolean>;
  isScrolled = false;

  constructor(private authService: AuthService) {
    this.isLoggedIn = this.authService.isLoggedIn;
  }

  @HostListener('window:scroll', [])
  onWindowScroll(): void {
    this.isScrolled = window.scrollY > 50;
  }

  logout(): void {
    this.authService.logout();
  }
}
