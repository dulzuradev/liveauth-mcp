import { ComponentFixture, TestBed } from '@angular/core/testing';

import { AdminAuthsLineChart } from './admin-auths-line-chart';

describe('AdminAuthsLineChart', () => {
  let component: AdminAuthsLineChart;
  let fixture: ComponentFixture<AdminAuthsLineChart>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AdminAuthsLineChart]
    })
    .compileComponents();

    fixture = TestBed.createComponent(AdminAuthsLineChart);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
