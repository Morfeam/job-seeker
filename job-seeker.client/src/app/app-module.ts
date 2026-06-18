import { NgModule, provideBrowserGlobalErrorListeners } from '@angular/core';
import { BrowserModule } from '@angular/platform-browser';
import { ReactiveFormsModule } from '@angular/forms';
import { HttpClientModule } from '@angular/common/http';

import { AppRoutingModule } from './app-routing-module';
import { App } from './app';
import { Navbar } from './navbar/navbar';
import { Home } from './home/home';
import { About } from './about/about';
import { SignIn } from './sign-in/sign-in';
import { SignUp } from './sign-up/sign-up';
import { ForgotPassword } from './forgot-password/forgot-password';
import { ResetPassword } from './reset-password/reset-password';
import { Dashboard } from './dashboard/dashboard';
import { UserJobs } from './user-jobs/user-jobs';
import { ErrorPage } from './error/error';

@NgModule({
  declarations: [
    App,
    Navbar,
    Home,
    About,
    SignIn,
    SignUp,
    ForgotPassword,
    ResetPassword,
    Dashboard,
    UserJobs,
    ErrorPage,
  ],
  imports: [
    BrowserModule,
    ReactiveFormsModule,
    HttpClientModule,
    AppRoutingModule,
  ],
  providers: [provideBrowserGlobalErrorListeners()],
  bootstrap: [App],
})
export class AppModule {}
