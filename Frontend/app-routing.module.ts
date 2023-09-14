import { NgModule } from '@angular/core';
import { Routes, RouterModule } from '@angular/router';

import { PageNotfoundComponent } from '@app/shared/components';
import { AccountLayoutComponent } from '@app/layouts';
import { PreloadModuleStrategy } from '@app/core/strategies';
import { AuthGuard } from '@app/core/guards';
import { Role } from '@app/shared/enums';
import { DashboardComponent, HelpComponent, JobsComponent, LogsComponent, CombineServicesComponent } from '@app/components';
import { Page } from '@app/shared/constants';
import { Destinations } from './components/destinations/destinations.module';

const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: '/dashboard' },
  {
    path: '',
    component: AccountLayoutComponent,
    data: { shouldAuthorized: true },
    canActivate: [AuthGuard],
    children: [
      {
        canActivateChild: [AuthGuard],
        data: { shouldAuthorized: true },
        path: Page.dashboard,
        component: DashboardComponent
      },
      {
        canActivateChild: [AuthGuard],
        data: {
          shouldAuthorized: true,
          roles: [Role.IsMatrix, Role.IsRoadSide]
        },
        path: Page.help,
        component: HelpComponent
      },
      {
        canActivateChild: [AuthGuard],
        path: Page.customers,
        data: {
          preload: true,
          roles: [Role.SuperAdmin]
        },
        loadChildren: () => import('./components/customers/customer.module').then(mod => mod.CustomerModule)
      },
      
      {
        path: Page.lineMaps.lineMaps,
        data: {
          preload: true,
          roles: [Role.IsRoadSide]
        },
        canActivateChild: [AuthGuard],
        loadChildren: () => import('./components/linemaps/linemaps.module').then(mod => mod.LineMapsModule)
      },
      {
        path: Page.services.services,
        data: {
          preload: true,
          roles: [Role.IsRoadSide, Role.IsMatrix]
        },
        canActivateChild: [AuthGuard],
        loadChildren: () => import('./components/services/services.module').then(mod => mod.Services)
      },
      {
        path: Page.datasets.datasets,
        data: {
          preload: true,
          roles: [Role.IsRoadSide, Role.IsMatrix]
        },
        canActivateChild: [AuthGuard],
        loadChildren: () => import('./components/datasets/datasets.module').then(mod => mod.DataSets)
      },
      
      {
        path: Page.notes.notes,
        data: {
          preload: true,
          roles: [Role.IsRoadSide, Role.IsMatrix]
        },
        canActivateChild: [AuthGuard],
        loadChildren: () => import('./components/notes/notes.module').then(mod => mod.NotesModule)
      },
      {
        path: Page.imageSet.imageSets,
        data: {
          preload: true,
          roles: [Role.IsRoadSide, Role.IsMatrix]
        },
        canActivateChild: [AuthGuard],
        loadChildren: () => import('./components/image-sets/image-set.module').then(mod => mod.ImageSetModule)
      },
      {
        path: Page.welcome,
        data: {
          preload: true,
          roles: [Role.IsRoadSide, Role.IsMatrix, Role.SuperAdmin, Role.Admin]
        },
        canActivateChild: [AuthGuard],
        loadChildren: () =>
          import('./components/welcome-page/welcome-page.module').then(mod => mod.WelcomePageModule)
      },
      {
        canActivateChild: [AuthGuard],
        data: { shouldAuthorized: true },
        path: Page.logs,
        component: LogsComponent
      },
      {
        canActivateChild: [AuthGuard],
        data: { shouldAuthorized: true },
        path: Page.combineServices,
        component: CombineServicesComponent
      }
    ]
  },
  { path: '**', component: PageNotfoundComponent }
];

@NgModule({
  imports: [
    RouterModule.forRoot(routes, {
      preloadingStrategy: PreloadModuleStrategy
      //enableTracing: true
    })
  ],
  exports: [RouterModule],
  providers: [PreloadModuleStrategy]
})
export class AppRoutingModule {}
