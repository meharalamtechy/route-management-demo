import { NgModule } from '@angular/core';
import { SharedModule } from '@app/shared';
import { OperatingProfilesRoutingModule } from './operating-profiles-routing.module';


@NgModule({
  imports: [
    SharedModule,
    OperatingProfilesRoutingModule
  ],
  declarations: [OperatingProfilesRoutingModule.components]
})
export class OperatingProfiles { }
