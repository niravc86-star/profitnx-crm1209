# ProfitNx CRM — Role-wise Visibility, Follow-ups & Training (2026-09-01)

## Summary of changes

### 1. Role-wise inquiry visibility (backend-enforced)
- Added `IInquiryService.GetVisibleForRoleAsync(role, userId)` as the single source of truth for inquiry data scope.
- `SearchAsync` now always starts from that scoped set (not from full sheet then frontend hide).
- `CanAccessInquiryAsync` (details/edit/delete/status) already reuses `SearchAsync`, so direct ID / URL / API manipulation is blocked for out-of-scope inquiries.
- Dashboard, Reports, and Live dashboard `GetVisibleInquiriesAsync` helpers now call the same service method (no more duplicated, divergent filters).

#### Per-role rules (Admin unchanged)
| Role | Visibility |
|------|------------|
| **Admin** | All inquiries (unchanged) |
| **Support Head** | Support Team ownership only: `InquirySource = Support Team`, non-empty `SupportExecutiveName`, or assigned/forwarded to a Support / SupportHead user. Does **not** include arbitrary Admin/Partner/User inquiries merely because Support opened them. |
| **Support** | Own assigned / forwarded / SupportExecutiveName / attended-by-name matches only |
| **Partner** | Own partner-forwarded / assigned inquiries only |
| **User (Customer)** | Own assigned / forwarded inquiries only |

### 2. Follow-up reminder visibility
- Login flash (`FollowUpFlash`), Inquiry list reminder cards, and Dashboard Today/Range follow-ups all derive from role-scoped inquiry sets.
- Partner / User only see their own follow-ups.
- Support / Support Head only see follow-ups on inquiries inside their support scope (not unrelated customer/partner work).
- Admin still sees system-wide follow-ups.

### 3. Training assignment + reminders
- Existing **Implementation & Training** module already supports:
  - Support Head assigns Support members
  - Schedule with topic, date, time, status, notes
  - `ImplementationReminderWorker` + `ProcessDueRemindersAsync` (today/approaching, no duplicate once `ReminderSentAt` is set)
  - Recipients: Support Head on case, Assigned member, Sold-by, Creator, Admins
- Assigned Support users only receive reminders for their own scheduled sessions via `ResolveRecipients` + `GetUpcomingForUserAsync` scoping.
- No parallel notification system was introduced.

### 4. Today Follow-ups / View Today button contrast
- Replaced `btn-outline-dark` with theme-aware class `btn-followup-today`.
- New stylesheet: `wwwroot/css/theme-followup-button-contrast-2026-09-01.css`
  - Premium Elite: solid blue, white text, readable hover/active/focus
  - Midnight: light chip on dark surface, dark text, readable hover/active/focus
  - Alert-warning context still high-contrast
- Linked from `_Layout.cshtml`.

### 5. Admin preservation
- Admin path still uses `GetAllAsync()` / full scope everywhere.
- No Admin permission, report, dashboard, or filter behavior was intentionally altered.

## Files changed
- `Services/IInquiryService.cs`
- `Services/InquiryService.cs`
- `Services/DashboardService.cs`
- `Controllers/DashboardController.cs`
- `Controllers/ReportsController.cs`
- `Views/Shared/_Layout.cshtml`
- `Views/Inquiry/Index.cshtml`
- `wwwroot/css/theme-followup-button-contrast-2026-09-01.css` (new)

## Database / Google Sheets
- **No schema or column changes.** Visibility is query-level only.

## How to run in Visual Studio
1. Open `ProfitNx.CRM.sln`
2. Ensure `appsettings.json` / Google service account still point at your sheet
3. Set startup project `ProfitNx.CRM`
4. F5 / Debug
5. Log in as each role and verify list, report, details URL with foreign IDs, follow-up flash, and Implementation training reminders

## Testing checklist
- [ ] Admin: all inquiries + all follow-ups unchanged
- [ ] Support Head: only Support Team scope; no pure Admin/Partner/User inquiries
- [ ] Support User: only own assigned/owned; foreign inquiry ID → Access Denied
- [ ] Partner: only own; foreign ID blocked
- [ ] User: only own; foreign ID blocked
- [ ] Follow-up flash counts match role scope
- [ ] Premium Elite + Midnight: "Today Follow-ups" / "View Today" readable (normal/hover)
- [ ] Training schedule reminder still fires once for assigned member + Support Head

## Fix pass 2 (2026-09-01 afternoon)

### Root cause of Admin inquiry leaking to Support Head
On create, `BuildInquiryAsync` always sets:
- `SupportExecutiveName` = creator name (even when creator is Admin)
- `AssignedUserId` = creator id

The first filter treated **any non-empty SupportExecutiveName** as Support ownership, so **every Admin-created inquiry matched**.

### Fix
`IsSupportTeamOwnedInquiry` now only accepts SupportExecutiveName / AssignedUserName when the value matches an **actual Support or SupportHead user** account. Admin/Partner names no longer match.

Support Head still sees:
- InquirySource = "Support Team"
- Assigned/forwarded to Support/SupportHead user ids
- SupportExecutiveName matching a real Support/SupportHead person

### Smart filter cards contrast
`conversion-focus-item` (Hot Leads, Overdue Follow-up, Missing Next Action, Negotiation) now has explicit high-contrast rules for Premium Elite and Midnight in `theme-followup-button-contrast-2026-09-01.css`.

## Fix pass 3 (Genuine Status + Remarks + Support Head reminders)

### Genuine Status
- Action column dropdown **only when InquiryQuality is empty (Pending)**
- Once set to Genuine / Not Genuine, dropdown is hidden on next load

### Update Remarks
- Small single-line input replaced with multi-line textarea
- **Expand** button opens full remarks modal window so entire typed text is visible
- Apply copies text back to the row form field

### Support Head reminders
- **No longer** shows inquiry Follow-up Reminder banners (login flash + Inquiry page Today Follow-up)
- Instead shows **Implementation / Training** flash:
  - Awaiting assignment (Admin new implementation requests)
  - Overdue / Today / Next 7 days training sessions (all team)
- Support users: own inquiry follow-ups + own training flash
- Partner / User / Admin: unchanged inquiry follow-up behavior

### Implementation visibility
- Support Head can see **all** implementation cases including unassigned (pending assignment)
