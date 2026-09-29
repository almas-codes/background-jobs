using BackgroundJobs.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BackgroundJobs.Dashboard;

public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapBackgroundJobsDashboard(
        this IEndpointRouteBuilder endpoints, string basePath = "/jobs")
    {
        var group = endpoints.MapGroup(basePath);

        group.MapGet("/api/stats",  async (IJobStorage s) =>
            Results.Ok(await s.GetStatsAsync()));

        group.MapGet("/api/jobs", async (IJobStorage s, JobStatus? status, int skip = 0, int take = 50) =>
            Results.Ok(await s.GetJobsAsync(status, skip, take)));

        group.MapGet("/api/jobs/{id}", async (string id, IJobStorage s) =>
            await s.GetAsync(id) is { } job ? Results.Ok(job) : Results.NotFound());

        group.MapPost("/api/jobs/{id}/requeue", (string id, IJobClient c) => Results.Ok(c.Requeue(id)));
        group.MapPost("/api/jobs/{id}/cancel",  (string id, IJobClient c) => Results.Ok(c.Cancel(id)));
        group.MapDelete("/api/jobs/{id}",       (string id, IJobClient c) => Results.Ok(c.Delete(id)));

        group.MapGet("/", () => Results.Content(DashboardHtml.Page, "text/html"));

        return endpoints;
    }
}

internal static class DashboardHtml
{
    public const string Page = """
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8" />
          <meta name="viewport" content="width=device-width, initial-scale=1" />
          <title>BackgroundJobs Dashboard</title>
          <style>
            *, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }
            body { font-family: system-ui, sans-serif; background: #0f172a; color: #e2e8f0; padding: 2rem; }
            h2 { margin-bottom: 1.5rem; font-size: 1.4rem; font-weight: 700; }
            #stats { display: flex; gap: 1rem; flex-wrap: wrap; margin-bottom: 2rem; }
            .stat { background: #1e293b; border-radius: 10px; padding: 1rem 1.5rem; min-width: 120px; border: 1px solid #334155; }
            .stat label { display: block; font-size: 11px; text-transform: uppercase; color: #64748b; margin-bottom: .25rem; letter-spacing: .05em; }
            .stat span { font-size: 2rem; font-weight: 800; }
            table { width: 100%; border-collapse: collapse; font-size: 13px; }
            th { text-align: left; padding: .6rem .75rem; border-bottom: 2px solid #1e293b; font-size: 11px; text-transform: uppercase; color: #64748b; letter-spacing: .05em; }
            td { padding: .6rem .75rem; border-bottom: 1px solid #1e293b; vertical-align: middle; }
            tr:hover td { background: #1e293b44; }
            .badge { display: inline-block; padding: 2px 9px; border-radius: 999px; font-size: 11px; font-weight: 600; }
            .s0{background:#1e3a5f;color:#93c5fd}.s1{background:#1e3a5f;color:#60a5fa}
            .s2{background:#14532d;color:#86efac}.s3{background:#7f1d1d;color:#fca5a5}
            .s4{background:#78350f;color:#fcd34d}.s5{background:#1e293b;color:#94a3b8}
            .s6{background:#7c2d12;color:#fdba74}
            button { cursor: pointer; background: #1e293b; color: #cbd5e1; border: 1px solid #334155; border-radius: 6px; padding: 3px 10px; font-size: 12px; }
            button:hover { background: #334155; }
            #refresh-info { float: right; font-size: 12px; color: #64748b; }
          </style>
        </head>
        <body>
          <h2>&#9881;&#65039; BackgroundJobs Dashboard <span id="refresh-info">Auto-refresh every 2s</span></h2>
          <div id="stats"></div>
          <table>
            <thead>
              <tr>
                <th>ID</th><th>Type</th><th>Queue</th><th>Priority</th>
                <th>Status</th><th>Scheduled</th><th>Retries</th><th>Actions</th>
              </tr>
            </thead>
            <tbody id="rows"></tbody>
          </table>
          <script>
            const statusNames   = ['Scheduled','Processing','Succeeded','Failed','AwaitingRetry','Deleted','DeadLetter'];
            const priorityNames = ['Low','Normal','High','Critical'];
            const statKeys      = ['scheduled','processing','succeeded','failed','awaitingRetry','deadLetter'];

            async function refresh() {
              try {
                const stats = await fetch('api/stats').then(r => r.json());
                document.getElementById('stats').innerHTML = statKeys.map(k =>
                  `<div class="stat"><label>${k}</label><span>${stats[k] ?? 0}</span></div>`).join('');
              } catch {}

              try {
                const jobs = await fetch('api/jobs?take=100').then(r => r.json());
                document.getElementById('rows').innerHTML = (Array.isArray(jobs) ? jobs : []).map(j => `
                  <tr>
                    <td title="${j.id}"><code>${j.id.slice(0,8)}&hellip;</code></td>
                    <td>${(j.typeName || '').split(',')[0].split('.').pop()}</td>
                    <td>${j.queue}</td>
                    <td>${priorityNames[j.priority] ?? j.priority}</td>
                    <td><span class="badge s${j.status}">${statusNames[j.status]}</span></td>
                    <td>${new Date(j.scheduledAt).toLocaleString()}</td>
                    <td>${j.retryCount}/${j.maxRetries}</td>
                    <td>
                      <button onclick="act('${j.id}','requeue')">Requeue</button>
                      <button onclick="act('${j.id}','cancel')">Cancel</button>
                      <button onclick="act('${j.id}','delete')">Delete</button>
                    </td>
                  </tr>`).join('');
              } catch {}
            }

            async function act(id, action) {
              const method = action === 'delete' ? 'DELETE' : 'POST';
              const path   = action === 'delete' ? `api/jobs/${id}` : `api/jobs/${id}/${action}`;
              await fetch(path, { method }).catch(() => {});
              refresh();
            }

            refresh();
            setInterval(refresh, 2000);
          </script>
        </body>
        </html>
        """;
}
