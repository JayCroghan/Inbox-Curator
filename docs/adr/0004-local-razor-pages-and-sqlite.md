# ADR 0004: Loopback Razor Pages application with SQLite

- Status: Accepted
- Date: 2026-08-19

## Context

MAIL-001 is a single-user local census. It needs a readable dashboard, background work, relational aggregation, migrations, and minimal operational surface.

## Decision

Use ASP.NET Core Razor Pages on .NET 10, an in-process `BackgroundService`, EF Core, and SQLite. Kestrel listens explicitly on `IPAddress.Loopback`; a configurable port does not change the interface. Razor renders search, sort, and paging server-side. A small local script handles scan-page refresh and page-size submission under a restrictive content security policy.

## Consequences

- Deployment is one local web process and one database file.
- No external UI build chain or JavaScript framework is required.
- SQLite is appropriate for one process and mailbox; multi-user or distributed scanning would require a different concurrency and storage decision.
