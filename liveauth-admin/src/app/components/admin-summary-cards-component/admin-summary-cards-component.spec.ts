import { ComponentFixture, TestBed } from '@angular/core/testing';

import { AdminSummaryCardsComponent } from './admin-summary-cards-component';

describe('AdminSummaryCardsComponent', () => {
  let component: AdminSummaryCardsComponent;
  let fixture: ComponentFixture<AdminSummaryCardsComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AdminSummaryCardsComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(AdminSummaryCardsComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
