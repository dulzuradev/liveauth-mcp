import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-blog',
  standalone: true,
  imports: [CommonModule, RouterLink],
  template: `
    <div class="blog-container">
      <header class="blog-header">
        <a routerLink="/" class="back-link">← Back to LiveAuth</a>
        <h1>Blog</h1>
      </header>
      
      <div class="blog-posts">
        <article class="blog-post">
          <h2>How to Auth Your MCP Agent with Lightning</h2>
          <p class="date">March 3, 2026</p>
          <p class="excerpt">
            Add Lightning Network payments to your AI agents in 5 minutes using the LiveAuth MCP server.
            Proof-of-work + Lightning authentication for AI agents.
          </p>
          <a routerLink="/docs/blog/2026-03-03-mcp-lightning-auth" class="read-more">Read more →</a>
        </article>
      </div>
    </div>
  `,
  styles: [`
    .blog-container {
      max-width: 800px;
      margin: 0 auto;
      padding: 2rem;
      font-family: 'Roboto', sans-serif;
    }
    .blog-header {
      margin-bottom: 2rem;
      border-bottom: 1px solid #333;
      padding-bottom: 1rem;
    }
    .back-link {
      color: #00C2FF;
      text-decoration: none;
      font-size: 0.9rem;
    }
    .blog-post {
      background: #11182d;
      padding: 1.5rem;
      border-radius: 8px;
      margin-bottom: 1rem;
    }
    .blog-post h2 {
      margin: 0 0 0.5rem;
      color: #e3e7ee;
    }
    .date {
      color: #8b95a5;
      font-size: 0.85rem;
      margin-bottom: 1rem;
    }
    .excerpt {
      color: #e3e7ee;
      line-height: 1.6;
    }
    .read-more {
      display: inline-block;
      margin-top: 1rem;
      color: #00C2FF;
      text-decoration: none;
    }
  `]
})
export class BlogComponent {}
