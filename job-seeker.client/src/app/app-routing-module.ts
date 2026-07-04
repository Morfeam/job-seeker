import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';

import { Home } from './home/home';
import { About } from './about/about';
import { SignIn } from './sign-in/sign-in';
import { SignUp } from './sign-up/sign-up';
import { ForgotPassword } from './forgot-password/forgot-password';
import { ResetPassword } from './reset-password/reset-password';
import { Dashboard } from './dashboard/dashboard';
import { UserJobs } from './user-jobs/user-jobs';
import { ErrorPage } from './error/error';
import { Criteria } from './criteria/criteria';
import { Resume } from './resume/resume';
import { JobDetails } from './job-details/job-details';

const routes: Routes = [
  { path: '', component: Home },
  { path: 'about', component: About },
  { path: 'sign-in', component: SignIn },
  { path: 'sign-up', component: SignUp },
  { path: 'forgot-password', component: ForgotPassword },
  { path: 'reset-password/:id', component: ResetPassword },
  { path: 'dashboard', component: Dashboard },
  { path: 'my-jobs', component: UserJobs },
  { path: 'criteria', component: Criteria },
  { path: 'resume', component: Resume },
  { path: 'job-details', component: JobDetails },
  { path: 'error', component: ErrorPage },
  { path: '**', redirectTo: '' },
];

@NgModule({
  imports: [RouterModule.forRoot(routes, { scrollPositionRestoration: 'top' })],
  exports: [RouterModule],
})
export class AppRoutingModule {}
