import { Injectable } from '@angular/core';
import { PreloadingStrategy, Route } from '@angular/router';
import { Observable, of } from 'rxjs';

import { LoggerService } from '@app/core/services';

@Injectable()
export class PreloadModuleStrategy implements PreloadingStrategy {
  constructor(private logger: LoggerService) {}

  preload(route: Route, load: () => Observable<any>): Observable<any> {
    if (route.data && route.data['preload']) {
      this.logger.log(`Preloaded Module: ${route.path}`);
      return load();
    } else {
      return of(null);
    }
  }
}
