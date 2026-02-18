import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';

export type TaskStatus = 'Backlog' | 'Ready' | 'InProgress' | 'Blocked' | 'Review' | 'Done';
export type TaskPriority = 'Low' | 'Medium' | 'High' | 'Critical';
export type TaskType = 'Feature' | 'Bug' | 'Infra' | 'Research';

export interface MissionTask {
  id: number;
  title: string;
  description?: string;
  priority: TaskPriority;
  type: TaskType;
  owner?: string;
  status: TaskStatus;
  createdAt: string;
  updatedAt: string;
  dueDate?: string;
}

export interface CreateTaskDto {
  title: string;
  description?: string;
  priority: TaskPriority;
  type: TaskType;
  owner?: string;
  dueDate?: string | Date;
}

export interface UpdateTaskDto {
  title?: string;
  description?: string;
  priority?: TaskPriority;
  type?: TaskType;
  owner?: string;
  status?: TaskStatus;
  dueDate?: string | Date;
}

export interface TaskFilter {
  type?: TaskType;
  priority?: TaskPriority;
  status?: TaskStatus;
}

@Injectable({ providedIn: 'root' })
export class MissionControlService {
  private readonly baseUrl = '/api/mission';

  private get headers(): HttpHeaders {
    const token = localStorage.getItem('auth_token') || '';
    return new HttpHeaders({
      'Content-Type': 'application/json',
      'Authorization': `Bearer ${token}`
    });
  }

  constructor(private http: HttpClient) {}

  getKanban(filter?: TaskFilter): Observable<Record<TaskStatus, MissionTask[]>> {
    const params = new URLSearchParams();
    if (filter?.type) params.set('type', filter.type);
    if (filter?.priority) params.set('priority', filter.priority);
    
    const query = params.toString();
    return this.http.get<Record<TaskStatus, MissionTask[]>>(
      `${this.baseUrl}/kanban${query ? '?' + query : ''}`,
      { headers: this.headers }
    );
  }

  getTasks(filter?: TaskFilter): Observable<MissionTask[]> {
    const params = new URLSearchParams();
    if (filter?.type) params.set('type', filter.type);
    if (filter?.priority) params.set('priority', filter.priority);
    if (filter?.status) params.set('status', filter.status);
    
    const query = params.toString();
    return this.http.get<MissionTask[]>(
      `${this.baseUrl}/tasks${query ? '?' + query : ''}`,
      { headers: this.headers }
    );
  }

  getTask(id: number): Observable<MissionTask> {
    return this.http.get<MissionTask>(`${this.baseUrl}/tasks/${id}`, { headers: this.headers });
  }

  createTask(dto: CreateTaskDto): Observable<MissionTask> {
    return this.http.post<MissionTask>(`${this.baseUrl}/tasks`, dto, { headers: this.headers });
  }

  updateTask(id: number, dto: UpdateTaskDto): Observable<MissionTask> {
    return this.http.put<MissionTask>(`${this.baseUrl}/tasks/${id}`, dto, { headers: this.headers });
  }

  moveTask(id: number, status: TaskStatus): Observable<MissionTask> {
    return this.http.patch<MissionTask>(`${this.baseUrl}/tasks/${id}/move`, { status }, { headers: this.headers });
  }

  deleteTask(id: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/tasks/${id}`, { headers: this.headers });
  }
}
