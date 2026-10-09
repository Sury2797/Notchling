# Notchling access and planned pricing

**Current source phase: public testing, beginning with the v0.3.3 candidate. All 21 catalog panels are free for everyone in Release and Debug.** No application account, subscription or owner-only unlock is needed for local tools. Checkout is paused. The candidate still needs Windows qualification before it replaces the last verified v0.2.11 download.

Testing access enables the supported controls; it does not supply provider accounts or pretend that disconnected sources work. Windows media requires a compatible player. Revenue requires your read-only Stripe reporting key, analytics requires your HTTPS endpoint and bearer token, and calendar/coding require selected supported files. Weather still requires the configured licensed service and authenticated session; that backend is not configured in the published application.

Public testing does not create a paid entitlement, automatically charge anyone or promise permanent access to future editions. The owner may introduce the commercial split in a later release. Existing local material, privacy controls and recovery/export remain protected.

## Planned commercial editions

The intended later offering is a deliberately small **Free** edition and **Premium for US$2 per month**, billed monthly. The table below describes that future split; these feature paywalls are inactive during public testing. Commercial charging also requires verified billing service configuration, signing and customer policies.

| Capability | Free — US$0 | Premium — US$2/month |
| --- | --- | --- |
| Compact notch, basic navigation | Included | Included |
| Basic media transport | Play/pause, previous, next | Included |
| Full media controls | — | Artwork, seeking, timeline and volume, subject to player/device capability |
| Focus | One Pomodoro | Countdown, stopwatch/laps and hydration, plus Pomodoro |
| Quick capture | One local scratchpad | Notes, scratchpad, shelf and saved shortcuts |
| Extended utilities | — | Clipboard, system tools, conversions, emoji, sounds and Awake |
| Connected/imported tools | — | Supported revenue, analytics, coding, calendar and licensed weather |
| Privacy, keyboard access and reduced motion | Included | Included |
| Existing-data recovery/export | Included | Included |

External provider accounts, API charges, AI credits and hosted-service allowances are separate. Premium includes no cloud sync, unlimited storage, annual plan or lifetime offer. Unsupported providers are not included integration promises.

## Retained paid-entitlement policy

The desktop accepts actual paid Premium status only from device-bound RSA-signed proofs supplied by the configured publisher service. Public-testing tool access is a separate, explicit product phase and never rewrites those proofs. Restoring the future commercial gates therefore preserves the existing trust model. Production billing requires a US$2 USD recurring monthly Stripe price and hosted checkout; Stripe/webhook/signing/email/weather secrets remain on the server. A label, arbitrary local setting or sample-data preview does not establish a purchase.

The prepared subscription service supports three verified devices, replaces the oldest verified device when a fourth logs in, refreshes proof after six hours and permits up to 24 additional hours offline. Proofs expire no later than the verified paid period end. In the future commercial phase, temporary connectivity is distinct from a signed Free response; unverified, expired or wrong-device proofs do not grant paid access. Reinstallation restores through email verification. These rules must appear in checkout/customer disclosures before activation.

Cancellation stops future renewal and keeps paid access until the confirmed period end. A full refund or dispute revokes Premium at authoritative validation; a partial refund retains access. Past-due/unpaid/ended subscriptions return to Free. The service re-queries Stripe for state so duplicate or out-of-order webhook deliveries cannot independently grant access.

In the commercial phase, downgrade preserves local material for recovery/export while Premium editing and extended controls are gated. Public testing keeps those tools open independently of subscription expiry. Safety, privacy and accessibility standards remain available in both phases. Synthetic billing tests do not establish that production purchase and refund paths work.

## Owner setup before charging

Publish the legal publisher identity, private support/refund channel, applicable taxes and regional consumer disclosures. Approve [product terms](product-terms.md), including the proposed first-purchase refund window, and publish [privacy](privacy.md). Configure the production HTTPS domain, Stripe price/secrets, SMTP delivery, entitlement signing key, and licensed weather credentials using [billing setup](billing.md). Perform live sandbox lifecycle tests and separate native Windows 10/11 qualification.

The application's Revenue dashboard is separate: it reads the user's configured Stripe reporting account and shows captured payments after refunds; it does not bill Notchling customers or calculate MRR.
