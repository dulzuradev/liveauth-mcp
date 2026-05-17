import { ComponentFixture, TestBed } from '@angular/core/testing';

import { AdminAuthsLineChartComponent } from './admin-auths-line-chart';

describe('AdminAuthsLineChartComponent', () => {
  let component: AdminAuthsLineChartComponent;
  let fixture: ComponentFixture<AdminAuthsLineChartComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AdminAuthsLineChartComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(AdminAuthsLineChartComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
