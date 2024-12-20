import { CommonModule } from '@angular/common';
import { Component, Input, Output, OnInit,SimpleChanges,OnChanges } from '@angular/core';
import { EventEmitter } from '@angular/core';
import Point from '@arcgis/core/geometry/Point';
import { Observable, lastValueFrom } from 'rxjs';
import { GeocodeService } from 'src/app/services/geocode.service';
import { IonicsModule } from 'src/app/shared/ionics.module';
import { UI5Module } from 'src/app/shared/ui5.module';
import { FormsModule } from '@angular/forms';

@Component({
  selector: 'place-detail',
  templateUrl: './place-detail.component.html',
  styleUrls: ['./place-detail.component.scss'],
  standalone:true,
  imports:[
    CommonModule,
    UI5Module,
    FormsModule,
    IonicsModule,
  ]
})
export class PlaceDetailComponent  implements OnInit, OnChanges{

  constructor(private geocodeService: GeocodeService) { 

  }

  ngOnInit() {
    this.geocodeResult = this.geocodeService.locationToAddress(this.locationPoint);
    this.placeDetail = {
      name:"",
      category:"",
      description:"",
      formattedAddress: "",
      location:{
        latitude: this.locationPoint.latitude,
        longitude: this.locationPoint.longitude
      }
    }
  }

  async onSave() {
    const geocodeResult = await lastValueFrom(this.geocodeResult);

    this.placeDetail.formattedAddress = geocodeResult.LongLabel;

    this.save(this.placeDetail);
  }

  categoryChange(event:any) {
    const categoryList:[] = event.detail.items.map((detail:any) => detail.attributes.categoryId.value);
    this.placeDetail.category = categoryList.join(';');
  }

  onCancel() {
    this.cancel(null);
  }

  async ngOnChanges(changes: SimpleChanges) {
    this.placeDetail.location.latitude = this.locationPoint.latitude;
    this.placeDetail.location.longitude = this.locationPoint.longitude;
  }

  @Input()
  save: any

  @Input()
  cancel: any;

  @Input()
  locationPoint!:Point;

  isGeocoding:boolean = false;
  geocodeResult!: Observable<any>;

  placeDetail!:PlaceDetail;
}

export interface PlaceDetail {
  name: string,
  category: string,
  description: string,
  formattedAddress: string,
  location: {
    latitude: number,
    longitude: number
  }
}