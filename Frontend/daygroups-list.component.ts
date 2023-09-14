import { Component, OnInit, ViewChild } from '@angular/core';
import { DayGroupService } from '@app/core/services/dayGroup.service';
import { AddDaygroupsComponent } from '../add-daygroups/add-daygroups.component';
import { DayGroups } from '@app/shared/models/daygoups/dayGroup.model';
import { DayGroupInfo } from '@app/shared/models';
import { confirmationMessage } from '@app/shared/constants';
import { ConfirmationDialogService } from '@app/core/services';
import { DaygroupsInUseComponent } from '../daygroups-in-use/daygroups-in-use.component';
import { Observable } from 'rxjs';
import { NotificationService } from '@app/core/services';

@Component({
  selector: 'app-daygroups-list',
  templateUrl: './daygroups-list.component.html',
  styleUrls: ['./daygroups-list.component.scss']
})
export class DaygroupsListComponent implements OnInit {
  @ViewChild(AddDaygroupsComponent, { static: false })
  addDaygroupsComponent: AddDaygroupsComponent;
  @ViewChild(DaygroupsInUseComponent, { static: false })
  dayGroupsInUseComponent: DaygroupsInUseComponent;

  constructor(
    private dayGroupService: DayGroupService,
    private confirmationDialogService: ConfirmationDialogService,
    private notificationService: NotificationService
  ) {}
  daygroup: {
    id: '';
    dayGroupInfo: [];
    customerId: '';
  };
  servicesUsedByDayGroup: string[];
  dayGroups: DayGroups = this.daygroup;
  dayGroupInfo: DayGroupInfo[];

  ngOnInit() {
    this.init();
  }

  init() {
    this.dayGroupService.getDayGroups().subscribe(
      (resp): DayGroups => {
        this.dayGroups = resp;
        this.dayGroupInfo = this.dayGroups.dayGroupInfo;
        return this.dayGroups;
      }
    );
  }

  openAddDayGroupModal() {
    this.addDaygroupsComponent.openDayGroupModal();
  }

  deleteDayGroup = (dayGroupId: string): Observable<any> => {
    return this.dayGroupService.deleteDayGroup(dayGroupId);
  }

  moveDayGroup = (index, moveBy) => {
    const dayGroup = this.dayGroups.dayGroupInfo[index];
    this.dayGroups.dayGroupInfo.splice(index, 1);
    this.dayGroups.dayGroupInfo.splice(index + moveBy, 0, dayGroup);
    this.saveDayGroup(this.dayGroups);
  }

  saveDayGroup = (dayGroup) => {
    this.dayGroupService.saveDayGroups(dayGroup).subscribe(resp => {
      this.notificationService.success('Day Groups updated successfully');
      this.init();
    });
  }

  checkDayGroupCanBeDeleted = (dayGroupId: any, dayGroupName: string) => {
    this.dayGroupService
      .checkDayGroupCanBeDeleted(dayGroupId)
      .subscribe(resp => {
        this.servicesUsedByDayGroup = resp;
        if (this.servicesUsedByDayGroup.length > 0) {
          this.dayGroupsInUseComponent.openDayGroupInUseModal(
            this.servicesUsedByDayGroup
          );
        } else {
          const initialState = Object.assign({}, confirmationMessage.dayGroup);
          initialState.message = `${initialState.message} '${dayGroupName}'?`;
          this.confirmationDialogService
            .showConfirmationPopup(initialState)
            .content.onClose.subscribe(result => {
              if (result) {
                this.deleteDayGroup(dayGroupId).subscribe(resp => {
                  this.init();
                });
              }
            });
        }
      });
  };
}
