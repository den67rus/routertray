# RouterTray Privacy Policy

Last updated: 12 October 2026.

RouterTray is an independent Windows utility published by **den67rus**.
This notice describes official Microsoft Store and other official Windows
builds. Modified builds and third-party services may have different practices.
RouterTray is not affiliated with or endorsed by Keenetic or Netcraze.

Privacy contact: [den67rus@gmail.com](mailto:den67rus@gmail.com).

## Information used and why

RouterTray reads Windows network names and identifiers, network adapter
identifiers, MAC addresses, and gateway addresses to identify the selected
computer and choose its router profile. You supply router addresses, profile
names, and a router username and password or an access token. The application
uses these details to authenticate with the router you configure.

Router responses provide policy names, current assignments, and device names,
MAC addresses, and registration details. A response may contain information
about other devices on that router; RouterTray uses it to find the selected
device. Selecting a policy sends that device's MAC address and the selected
policy to the router. If you explicitly register a device during setup, the
application sends its MAC address and your chosen device name and may ask
the router to save its configuration.

The application has no developer-operated backend, advertising or analytics
SDK, and does not automatically upload settings, credentials, or diagnostic
logs to the developer. It does not inspect browsing history or the contents
of your internet traffic. Local router operations do not send those details
to the developer.

## Router connections and external services

Router requests use the address you configure or the detected gateway and the
router's authentication endpoints. This is normally a local network connection,
but a manually entered address can point elsewhere. The owner of that endpoint
receives the requests. Choose only a router you are authorized to manage.

Connections use HTTP or HTTPS according to the configured address. HTTPS uses
Windows certificate validation. HTTP does not encrypt the transport: someone
able to observe the connection may read authentication tokens, identifiers,
commands, and responses. Prefer a trusted HTTPS endpoint where supported, and
do not use HTTP over an untrusted network. No software or encryption mechanism
can guarantee protection against every attack or compromise.

Microsoft Store builds receive application updates through Microsoft Store
and do not check GitHub Releases. Other installed builds can contact GitHub
to check for and download updates, according to their update settings.
Microsoft, GitHub, email providers, your router, and other services you choose
may receive ordinary connection information, such as IP addresses, and process
information under their own terms and privacy notices. Those services may
process information in countries different from yours. Opening an external
link or contacting support uses the service you choose.

## Local storage, security, and retention

Profiles and preferences are stored in the current Windows user's local
application data. Saved passwords and access tokens are protected with
Windows Data Protection API (DPAPI), scoped to that Windows account. Profile
names, router addresses, usernames, network bindings, and other preferences
are ordinary settings and are not all encrypted. Secrets must also be
available to the application in memory while authenticating.

DPAPI is not protection against every threat, including malware running as
the same Windows user, a compromised Windows account, or a compromised
computer. You control Windows account access, router permissions, backups,
and the endpoints you configure. The developer cannot remotely access or
erase files that remain only on your computer.

Diagnostic logs remain on the computer. They can contain timestamps, network,
adapter, profile and policy identifiers, errors, and details returned by the
router. Logs are not a secure vault, and unexpected router responses may
contain sensitive information. Review and redact logs before sharing them.

Settings, backup copies, logs, and preserved corrupt settings files can remain
until you or Windows remove them. Removing a profile does not necessarily
remove older copies. Uninstalling the application does not guarantee deletion
of every backup, exported file, router-side record, or third-party copy.

## Your choices and deletion

You can edit or remove profiles, choose startup behavior, and change policy
notifications in Settings. Other installed builds also offer update settings;
Microsoft Store updates are controlled by Windows and Microsoft Store.

Exit RouterTray before manually deleting local settings, logs, and backups.
The usual unpackaged folder is `%LOCALAPPDATA%\RouterTray`. Windows may
redirect Store files into the application's per-user package storage under
`%LOCALAPPDATA%\Packages`. Revoke router tokens or accounts in the router's
own administration interface: deleting a local profile does not revoke them.
Router configuration and registration records must be managed on the router.

## Information you choose to send to support

If you email the privacy contact or submit a GitHub issue, the developer can
receive your message, contact details, and any information you include. It is
used to respond, investigate problems, handle privacy or security requests,
and meet applicable legal obligations. Where a legal basis is required,
handling ordinary support requests relies on the legitimate interest in
responding and maintaining the software; legally required processing relies
on the applicable obligation. Consent is used where the law requires it.

Support information is kept only as reasonably necessary for those purposes,
including required legal retention and establishing or defending legal claims.
Service-provider backups or public copies can persist under the relevant
provider's policies. Necessary information may be handled by email and hosting
providers, or disclosed when required by law. It is not used for advertising.

GitHub issues are public unless the service explicitly says otherwise. Do not
post credentials, tokens, unredacted logs, or other people's personal data.
Use the privacy email for confidential requests and share only what is needed.

Where applicable law grants them, you may request access, correction, deletion,
restriction, portability, or objection concerning information the developer
holds, withdraw consent for processing based on consent, and complain to the
competent data protection authority. These rights depend on the law and the
processing involved. Contact the privacy email; do not publish identity
documents in an issue. For information held only locally, use the controls
above. For information held independently by a service provider, contact it.

## Software license and limits of responsibility

RouterTray is distributed under the [MIT License](https://github.com/den67rus/routertray/blob/main/LICENSE),
also included with the application. To the extent permitted by applicable
law, the software is supplied as is, without express or implied warranties,
including fitness for a particular purpose, merchantability, or non-infringement.
The authors and copyright holders disclaim liability for claims, damages,
and other liability arising from the software or its use as set out in that
license. This includes, where legally excludable, loss of data, profits,
connectivity, or privacy, interruptions, and effects of router configuration,
third-party failures, unauthorized access, or inability to use the software.

RouterTray selects policies that already exist on your router. It does not
provide a VPN service, anonymity, an encrypted traffic tunnel, or a guarantee
that a router will enforce a particular route or security policy. Policy
changes can affect connectivity and the providers through which traffic
passes. Verify the router's behavior and permissions and maintain appropriate
backups before relying on it. Voluntary support does not create a guarantee
of response times, continuing updates, or compatibility, except where the
law requires otherwise.

These statements do not limit the permissions granted by the MIT License.
Nothing in this notice excludes liability that cannot lawfully be excluded,
waives mandatory consumer or data protection rights, prevents complaints,
or excuses the developer from applicable legal obligations. The description
of security measures is not a promise of absolute security. This privacy
notice is not a substitute for the software license or third-party terms.

## Changes and contact

This notice will be updated when the application's data handling changes.
The date above identifies this version. Material changes requiring notice
or consent will be handled as required by applicable law; this notice does
not authorize undisclosed future collection or sharing.

Privacy requests: [den67rus@gmail.com](mailto:den67rus@gmail.com).
General support: [RouterTray issues](https://github.com/den67rus/routertray/issues).
