import { NgModule } from '@angular/core';
import { Routes, RouterModule } from '@angular/router';

import { RuleSetListComponent } from './rule-set-list/rule-set-list.component';
import { CopyRuleSetComponent } from './copy-rule-set/copy-rule-set.component';
import { EditRulesetComponent } from './edit-ruleset/edit-ruleset.component';
import { RulesetTemplateComponent } from './ruleset-template/ruleset-template.component';
import { RulesetLayoutsComponent } from './ruleset-layouts/ruleset-layouts.component';
import { RulesetImagesetComponent } from './ruleset-imageset/ruleset-imageset.component';
import { RulesetLinemapComponent } from './ruleset-linemap/ruleset-linemap.component';
import { RulesetCompressionComponent } from './ruleset-compression/ruleset-compression.component';
import { AddRuleModalComponent } from './add-rule-modal/add-rule-modal.component';

const routes: Routes = [
  {
    path: '',
    children: [
      {
        path: '',
        component: RuleSetListComponent
      },
      {
        path: ':id',
        component: EditRulesetComponent
      }
    ]
  }
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule]
})
export class RuleSetRoutingModule {
  static components = [
    RuleSetListComponent,
    CopyRuleSetComponent,
    EditRulesetComponent,
    RulesetTemplateComponent,
    RulesetLayoutsComponent,
    RulesetImagesetComponent,
    RulesetLinemapComponent,
    RulesetCompressionComponent,
    AddRuleModalComponent
  ];
}
