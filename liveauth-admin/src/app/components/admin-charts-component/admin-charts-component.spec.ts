import { ComponentFixture, TestBed } from '@angular/core/testing';

import { AdminChartsComponent } from './admin-charts-component';

describe('AdminChartsComponent', () => {
  let component: AdminChartsComponent;
  let fixture: ComponentFixture<AdminChartsComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AdminChartsComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(AdminChartsComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
