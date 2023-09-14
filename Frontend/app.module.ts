import { BrowserModule } from '@angular/platform-browser';
import { BrowserAnimationsModule } from '@angular/platform-browser/animations';
import { NgModule, APP_INITIALIZER } from '@angular/core';
import { NgxPermissionsModule } from 'ngx-permissions';

import { AppComponent } from './app.component';
import { AccountLayoutComponent } from '@app/layouts';
import { CoreModule } from '@app/core';
import { SharedModule } from '@app/shared';
import { AppRoutingModule } from './app-routing.module';
import { AccountModule } from '@app/components/account';
import { DashboardComponent, HelpComponent, JobsComponent, LogsComponent, CombineServicesComponent } from '@app/components';
import { ConfigsLoaderService } from './core/services/configs-loader.service';

@NgModule({
  declarations: [AppComponent, AccountLayoutComponent, DashboardComponent, HelpComponent, JobsComponent, LogsComponent, CombineServicesComponent],
  imports: [
    BrowserModule,
    BrowserAnimationsModule,
    NgxPermissionsModule.forRoot(),
    AccountModule,
    AppRoutingModule,
    CoreModule,
    SharedModule
  ],
  providers: [
    {
      provide: APP_INITIALIZER,
      useFactory: appInitializerFactory,
      deps: [ConfigsLoaderService],
      multi: true
    }
  ],
  bootstrap: [AppComponent]
})
export class AppModule {}

export function appInitializerFactory(configsLoaderService: ConfigsLoaderService) {
  return () => configsLoaderService.loadConfigs();
}
