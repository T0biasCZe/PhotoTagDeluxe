# Photo Tag Deluxe
Application for easily taggining date time and gps when shooting on film. 

## Phone clients
Windows Phone 8.1+ client    
- Available on live store:    SOON
- Xap available on releases page           
Android client: SOON

## Metadata applier client
Available for Windows 7 64bit and newer
Download on releases page

Usage:
1) Click "import photos", and select folder where scans for 1 specific roll are. Do NOT mix different rolls in 1 folder
2) Click "Online data". Enter ptdx.tobikcze.eu, or your selfhosted server address (for example, 127.0.0.1 if running on your PC, or your domain, for example, ptdx.example.com), alongside your username and password
3) After logging, select which roll data you want to apply, and click Download
4) Modify file zone offset if needed.
5) I recommend checking "Rename file". It will name the files in FILM_YYYY-MM-DD_HH-MM-SS format.
6) Click "Save data". All the data will be injected into photos.

## Server for selfhost
As an alternative to using the centralized ptdx.tobikcze.eu server, you can selfhost your own:

Setup:
1) Download server from releases page
2) extract
3) configure config.txt

| Setting | Example value | Description |
|---|---|---|
| `MANDATORY_EMAIL` | `true`/`false` | Configures whether user must enter email when registering |
| `EMAIL_VERIFICATION` | `true/false` | Configures whether the email must be verified with a verification code. SMTP has to be configured for this |
| `SMTP_HOST` | | |
| `SMTP_PORT` | `465` | |
| `SMTP_USERNAME` | | |
| `SMTP_PASSWORD` | | |
| `MAX_THUMB_WIDTH` | `400` | Width in pixels at which the thumbnails shot in the client will be uploaded |
| `MAX_THUMB_SIZE_KB` | `20` | Max file size of the thumbnail |
| `MAX_THUMB_WIDTH_VIP` | `1920` | Same as above, but for "VIP" users |
| `MAX_THUMB_SIZE_KB_VIP` | `200` | Same as above |
| `VIP_USERS` | `testvip,tobiki` | All users that should be VIP, separated by `,`. Can be empty |

4) configure envsecrets.txt

| Setting | Example value | Description |
|---|---|---|
| `JWT_SECRET` | `89fg418fh14rh1r4hrh46h14r` | Set this to a long random string, used for encrypting login and communication data. The Value is example, do NOT copy this one!!!!|
| `USERDATA_PATH` | `C:\users\user\PTDX` | Set this to a path where data for all users will be stored |

5) start server by clicking PhotoTag.Server.exe

Then, there are three options for using the server, from most basic:

### 1. Local area network only
* will be available only when the PC and phone is on the same network.
* In the phone app, just enter your PCs ip address, for example, http://192.168.68.120:8069, and it will work
* To obtain your PCs IP, just open "cmd" and enter "ipconfig", it will be either under "Ethernet adapter" or "Wireless" adapter

### 2. Router port forwardin
* You need to have public IPv4 address.
* Go to your router settings, and in port forward section, enter your PCs address, for example 192.168.68.120, and enter the in and out port as 8069
* Then in phone app, enter http://YOURPUBLICIP:8069
* You will be able to use from anywhere.

### 3. Cloudflared tunnel
* Can be used from the whole internet, and you dont need public IPv4, however you must have a Cloudflare account (free) and own a domain (costs like 5€ for .eu)
* just configure cloudflared to point to your PCs ip, once again port 8069, set subdomain, for example ptdx, and in app, enter https://ptdx.yourdomain.com