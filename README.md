# ProfitNx CRM - Support Inquiry Flow + Sheet Template Version

## 1) Files included
- `profitnx-crm-support-flow-updated.zip` = complete standalone Visual Studio project.
- `profitnx-crm-google-sheet-template.xlsx` = ready Excel template with all required Google Sheet tabs and sample rows.

## 2) How to create Google Sheet from Excel template
1. Open Google Drive.
2. Upload `profitnx-crm-google-sheet-template.xlsx`.
3. Right click uploaded file -> Open with -> Google Sheets.
4. File -> Save as Google Sheets, if required.
5. Copy Spreadsheet ID from URL.
6. Put that ID in `appsettings.json` under `GoogleSheets:SpreadsheetId`.
7. Share the Google Sheet with your service account email as Editor.

## 3) Exact Google Sheet tabs and headers

### Users
`Id | FullName | Username | PasswordHash | Role | PartnerCode | Mobile | Email | City | IsActive | TargetAmount | MarginPercent | Notes`

### Roles
`Id | RoleName | IsSystemRole | IsActive | Notes`

### RolePermissions
`Id | RoleName | PermissionKey | IsAllowed`

### Inquiries
`Id | CreatedDate | FirmName | PersonName | City | Mobile1 | Mobile2 | Email1 | Email2 | ProductName | VersionType | Remarks | Status | StatusReason | ForwardedToPartnerId | ForwardedToPartnerName | AssignedUserId | AssignedUserName | AttendedByRole | NextFollowUpDate | DemoScheduledDate | DemoDoneDate | LicensesPurchased | LicenseNumber | BillNo | BillDate | AmountWithoutGst | AmountWithGst | SoldDate | ClosedDate | CloseReason | CustomerInfo | DealerId | DealerName | LastUpdated | SupportExecutiveName | InquirySource`

### InquiryUpdates
`Id | InquiryId | UpdateDate | UserId | UserName | Status | Note | NextFollowUpDate | LicenseNumber | BillNo | BillDate | CloseReason`

### Dealers
`Id | DealerName | ContactPerson | Mobile | Email | City | Address | IsActive | Notes`

### Products
`Id | Name | Version | Category | Price | IsActive | Notes`

### Stock
`Id | ProductId | ProductName | Version | Quantity | Unit | Location | LastUpdated | Notes`

### Schemes
`Id | SchemeName | Description | StartDate | EndDate | MarginPercent | IsActive`

### SchemeParticipation
`Id | SchemeId | SchemeName | UserId | UserName | Role | JoinedDate | IsActive | Notes`

### Notifications
`Id | SentDate | Channel | Recipient | Message | RelatedEntityType | RelatedEntityId | Status | ActionUrl`

## 4) Sample login rows included in template
The Excel template includes these sample users:
- Admin: username `admin`, password `admin123`
- Support: username `support`, password `support123`
- Partner: username `partner`, password `partner123`
- User: username `user`, password `user123`

This project version supports plain password during testing and BCrypt hash in production.

## 5) Support inquiry flow added
### Required workflow
1. Support team logs in with Support role.
2. Support team opens Inquiry -> Send New Inquiry.
3. Support team enters details like:
   - Date & Time
   - Contact Person Name
   - Contact Number
   - City Name
   - Business Name
   - Product / Version
   - Other Remark
   - Support Executive Name
4. Inquiry is saved in CRM and goes to Admin first.
5. Admin sees it in Inquiry List.
6. Admin can self-attend or forward to partner.
7. Partner receives CRM notification, WhatsApp Web link and email notification if configured.
8. Partner updates status.
9. Admin and support team can see status timeline date-wise.

### Support team report
Support role sees only their own submitted inquiries.
They can open inquiry and see:
- Current status
- Follow-up date
- Demo scheduled / demo done
- Sold / close details
- Timeline history

## 6) Inquiry status flow
Recommended flow:
1. New Inquiry
2. Follow Up
3. Demo Scheduled
4. Demo Done
5. Follow Up / Negotiation
6. Sold
7. Close

### Sold status requires:
- License Number
- Bill No
- Bill Date

### Close status requires:
- Close Reason

## 7) WhatsApp and Email notification note
Free WhatsApp Web cannot reliably auto-send from server in the background.
This project uses a practical free solution:
- CRM notification is saved in Notifications tab.
- WhatsApp Web clickable link is generated.
- User clicks link and sends through WhatsApp Web.

Email sending works if SMTP is configured in `appsettings.json`:
```json
"Smtp": {
  "Host": "smtp.gmail.com",
  "Port": "587",
  "From": "yourmail@gmail.com",
  "Username": "yourmail@gmail.com",
  "Password": "your-app-password"
}
```

## 8) Visual Studio setup
1. Extract ZIP.
2. Open `ProfitNx.CRM.csproj` in Visual Studio.
3. Put `google-service-account.json` in the same folder as `.csproj`.
4. Update `appsettings.json` with spreadsheet id.
5. Build -> Rebuild Solution.
6. Run.

## 9) Outside location / partner / support login
For outside login, publish this ASP.NET Core app to public hosting:
1. Visual Studio -> Publish -> Folder.
2. Upload published folder to Windows VPS / IIS hosting / Azure App Service.
3. Bind domain and HTTPS.
4. Open firewall port 80/443.
5. Give URL to partner/support/user.

Partners and support users can login from any location using the hosted CRM URL.

## 10) Mobile as app
Android Chrome:
1. Open hosted CRM URL.
2. Tap three dots.
3. Tap Add to Home Screen.
4. Use it like an app icon.

Windows Chrome:
1. Open CRM URL.
2. Three dots -> Save and share -> Create shortcut.
3. Tick Open as window.
