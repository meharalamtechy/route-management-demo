import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

import { Uri } from '@app/shared/constants';
import { ImageSets } from '@app/shared/models';
import { Images } from '@app/shared/models/timetable/images.model';
import { ConfigsLoaderService } from './configs-loader.service';

@Injectable()
export class ImageSetService {
  constructor(private http: HttpClient,private config: ConfigsLoaderService) {}

  getAllImageSets = (): Observable<ImageSets[]> => {
    return this.http.get<ImageSets[]>(`
    ${this.config.apiUrl}${Uri.ImageSet.getAllImageSets}
    `);
  };

  deleteImageSet(id: string) {
    const params = new HttpParams().set('id', id);
    return this.http.delete(`${this.config.apiUrl}${Uri.ImageSet.deleteImageSet}`, { params });
  }

  saveImageSet(imageSet: any) {
    return this.http.post(`${this.config.apiUrl}${Uri.ImageSet.addEditImageSet}`, imageSet, {
      responseType: 'text'
    });
  }

  getImageSet(id: string) {
    const params = new HttpParams().set('id', id);
    return this.http.get<ImageSets>(`${this.config.apiUrl}${Uri.ImageSet.getImageSet}`, { params });
  }

  validateImageSize(formData: any) {
    return this.http.put(`${this.config.apiUrl}${Uri.ImageSet.validateImageSize}`, formData);
  }

  saveImage(formData: any, imageSetId: string) {
    const params = new HttpParams().set('imageSetId', imageSetId);
    return this.http.put(`${this.config.apiUrl}${Uri.ImageSet.saveImage}`, formData, { params });
  }

  getImage(imageSetId: string, imageId: string): Observable<Images> {
    const params = new HttpParams().set('imageSetId', imageSetId).set('imageId', imageId);
    return this.http.get<Images>(`${this.config.apiUrl}${Uri.ImageSet.getImage}`, { params });
  }

  deleteImage(imageId: string, id: string) {
    const params = new HttpParams().set('imageSetId', id).set('imageId', imageId);
    return this.http.delete(`${this.config.apiUrl}${Uri.ImageSet.deleteImage}`, { params });
  }
}
