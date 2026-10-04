# Notchling Free and Premium

Notchling has a deliberately small **Free** edition and **Premium for US$2 per month**, billed monthly. Commercial charging is inactive until the publisher configures and verifies the billing service, signing and customer policies. Release starts Free; Debug visibly enables development access.

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

## Implemented access policy

The desktop accepts only device-bound RSA-signed proofs from the configured publisher service. Production billing requires a US$2 USD recurring monthly Stripe price and hosted checkout; Stripe/webhook/signing/email/weather secrets remain on the server. Configuring only a label, local flag or demo mode cannot grant Premium in Release.

The current service policy supports three verified devices, replaces the oldest verified device when a fourth logs in, refreshes proof after six hours and permits up to 24 additional hours offline. Proofs expire no later than the verified paid period end. Temporary connectivity is distinct from a signed Free response; unverified, expired or wrong-device proofs do not unlock tools. Reinstallation restores through email verification. These rules must appear in checkout/customer disclosures before activation.

Cancellation stops future renewal and keeps paid access until the confirmed period end. A full refund or dispute revokes Premium at authoritative validation; a partial refund retains access. Past-due/unpaid/ended subscriptions return to Free. The service re-queries Stripe for state so duplicate or out-of-order webhook deliveries cannot independently grant access.

On downgrade, local material remains available for recovery/export. Premium editing and extended controls are gated. Free safety/privacy/accessibility standards remain available. Debug full-catalog fixtures and synthetic billing tests do not establish that the production purchase and refund paths work.

## Owner setup before charging

Publish the legal publisher identity, private support/refund channel, applicable taxes and regional consumer disclosures. Approve [product terms](product-terms.md), including the proposed first-purchase refund window, and publish [privacy](privacy.md). Configure the production HTTPS domain, Stripe price/secrets, SMTP delivery, entitlement signing key, and licensed weather credentials using [billing setup](billing.md). Perform live sandbox lifecycle tests and separate native Windows 10/11 qualification.

The application's Revenue dashboard is separate: it reads the user's configured Stripe reporting account and shows captured payments after refunds; it does not bill Notchling customers or calculate MRR.
