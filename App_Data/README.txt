This folder stores runtime-settings.json.

You may also place the private Google service-account key here as:
    App_Data/google-service-account.json

The application will discover it automatically even when appsettings.json contains:
    "CredentialsFile": "google-service-account.json"

Never commit or share the real credential file. The project .gitignore excludes it.
