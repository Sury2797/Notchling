# Notchling billing and Premium verification

Notchling includes a separate ASP.NET Core service, a portable entitlement validator, and a desktop billing client. Production commerce remains **disabled until the owner supplies a domain, Stripe configuration, an email sender, durable storage, signing keys, customer policies, and licensed weather access**. This repository does not contain live credentials, activate a subscription, or deploy a service. An email login, browser return, local setting, and demo mode cannot grant a paid entitlement.

**Current product phase: public testing.** The v0.3.2 candidate grants all catalog tools to everyone in both Release and Debug through an explicit product-access policy. The desktop purchase controls and server checkout are paused; no fake Premium proof is issued. Optional service login remains available when the backend is configured, for authenticated services such as the licensed weather proxy. The current published configuration supplies no such backend. The older v0.2.11 download predates this phase until qualification publishes a replacement.

Live Stripe keys additionally require **Billing__CommercialReleaseApproved=true**, set only after the publisher identity, customer terms, private support/refund route, tax configuration, and native release qualification are completed. Test credentials do not activate live charges.

## Product policy implemented in code

| Rule | Behavior |
| --- | --- |
| Price | Checkout verifies an active Stripe price of **US$2 every month**, quantity one. Additional applicable taxes/customer disclosures must be configured before sale. |
| Public testing | All supported tools unlocked without payment. Provider credentials, file imports, native capabilities and service authentication still apply. Checkout is paused. |
| Future Free | Basic media transport, one Pomodoro and scratchpad, navigation, settings and accessibility. Commercial-phase gates protect extended features; existing work remains available for recovery/export. |
| Identity and restore | Verified email with an eight-digit, single-use code; reinstall can restore through the same email. No password or card data enters Notchling. |
| Devices | Three most recently verified devices. Verifying a fourth replaces the oldest login; its cached proof expires within the offline limit. |
| Session | A random 256-bit bearer session, hashed on the service and encrypted in Windows Credential Locker, expires after 30 days. |
| Proof | RSA-SHA256 signature, issuer/audience checks, version, account and device binding. Public verification key only in desktop. |
| Refresh and offline | Paid proof refresh after six hours; up to 24 additional hours offline, always capped by the paid period end. Invalid/expired proof does not grant Premium. Public-testing tool access is independent of paid status. Clock rollback beyond five minutes requires restoration. |
| Cancellation | Stripe customer portal manages cancellation. Period-end cancellation retains paid access until that period ends. Immediate cancellation removes online entitlement. |
| Failure/refund | Past-due/unpaid/canceled subscriptions grant no new Premium proof. A full refund or dispute on the latest paid invoice revokes access on refresh. A partial goodwill refund retains remaining paid access. Already issued offline proofs remain valid only for the bounded grace period. |
| Downgrade | Does not delete notes, reminders, links, shelf entries or scratchpad. Desktop recovery/export remains available without Premium. |

Client-side enforcement cannot resist a deliberately patched desktop executable. Paid-entitlement verification remains strict in every phase. The weather proxy always validates its authenticated session; during public testing it does not require a paid subscription, while the later commercial phase independently checks current Premium state. Provider licensing, authentication, rate limits and expiry remain enforced. Clock rollback checks make accidental local clock changes explicit; they are not hardware attestation.

## Service configuration

`src/Notch.Billing/appsettings.json` is deliberately empty for commercial settings. Supply secrets through a deployment secret manager/environment variables, never commit them or distribute them in the desktop:

| Variable | Requirement |
| --- | --- |
| `Billing__StripeSecretKey` | Stripe restricted/server key with customer, checkout, subscription, invoice/charge and portal access. Use test mode during staging. |
| `Billing__StripeWebhookSecret` | Signing secret for `/v1/stripe/webhook`. |
| `Billing__StripePriceId` | An active US$2/month USD recurring price. No trial is offered by this implementation. |
| `Billing__SigningPrivateKeyPem` | RSA PKCS#8 private PEM, at least 2048 bits; keep outside database and desktop. |
| `Billing__PublicOrigin` | HTTPS origin serving the application and `/billing/return`. No credentials, query or fragment. |
| `Billing__DataDirectory` | Owner-only, backed-up, durable directory; cannot be ephemeral container storage. |
| `Billing__SmtpHost`, `Billing__SmtpPort` | Transactional mail service supporting TLS/STARTTLS; port defaults to 587. |
| `Billing__SmtpUsername`, `Billing__SmtpPassword` | Email credentials from secret storage. |
| `Billing__MailFrom` | Verified sender with correct SPF/DKIM/DMARC configuration. |
| `Billing__MaximumDevices` | Defaults to three; allowed range 1–10. Published policy must match deployment. |
| `AllowedHosts` | The actual production hostname; repository default allows only localhost. |

Generate keys on a trusted administrator machine, exporting **only the public key** into the desktop public configuration. Preserve the signing key between deploys. A key rotation requires a desktop configuration update; coordinate expiry/transition before changing the active key. `/health/ready` returns 503 until configuration validates. Readiness validates local settings, not Stripe credentials, SMTP delivery, commercial agreements or support readiness.

The store uses serialized transactions, atomic replacement, write-through flushing and Unix owner-only permissions. It is designed for **one service process with one durable volume**. Do not start multiple replicas against this JSON store. Back up and restore the database with the signing key managed separately, restrict Windows ACLs when hosting on Windows, and migrate to a transactional database before horizontal scaling. A corrupt/unreadable state fails closed; never reset customer records to an empty database during recovery.

Put the service behind HTTPS termination, with direct outbound access to Stripe/SMTP/licensed weather. Configure trusted forwarded headers explicitly if deployed behind a proxy; until then the rate limiter uses the direct peer address. Never trust arbitrary client `X-Forwarded-For` values. Production logging must omit authorization headers, OTP bodies, private keys and weather request URLs; the implementation disables HttpClient URI logging. Monitor failed webhooks and service readiness without recording customer secrets.

## Stripe staging checklist

1. Create the USD 200-cent/month price and supply its ID. The service pins Stripe API version `2024-06-20`; verify account compatibility before live mode.
2. Configure the customer portal to allow payment updates and cancellation at the period end; disable arbitrary price switching/quantity changes for this single-plan product.
3. Configure checkout customer disclosures, customer terms/privacy links, receipt sender and applicable tax collection. The service does not determine tax jurisdiction or supply finalized legal policies.
4. Send subscription, invoice, checkout completion, charge refund and dispute events to `/v1/stripe/webhook`. Recommended events: `checkout.session.completed`, `customer.subscription.created`, `customer.subscription.updated`, `customer.subscription.deleted`, `invoice.paid`, `invoice.payment_failed`, `charge.refunded`, `charge.dispute.created`, `charge.dispute.closed` and `refund.updated`.
5. Webhook delivery must preserve raw request bytes and `Stripe-Signature`. The service checks HMAC-SHA256 signatures within five minutes, records processed IDs for 90 days, and **re-queries current Stripe state** for every customer event. Event timestamps/order never directly grant Premium. Provider failures return 503 for Stripe retry rather than acknowledge an unprocessed change.
6. Verify purchase, refresh, renewal, payment failure, period-end cancellation, immediate cancellation, full/partial refund, dispute, duplicate/out-of-order event, SMTP failure, reinstall, fourth-device replacement and offline expiry in Stripe test mode. Do not collect live payments before these checks and owner configuration are complete.

Customer creation uses a stable idempotency key. Checkout recovers an existing open session and refuses a second active/past-due/unpaid subscription for the same configured price. Browser success messages direct customers back to Notchling to refresh; they are never purchase verification.

## API contract

All bodies are JSON except Stripe's raw webhook. Authenticated requests require `Authorization: Bearer <session>`. Responses use `Cache-Control: no-store`. Login requests have five requests per IP/minute plus one challenge per email/minute; codes expire after ten minutes and five failed attempts. General API traffic is limited to 60 requests per IP/minute. Configure edge limits separately for abuse and webhook request volume.

| Method / path | Request | Result |
| --- | --- | --- |
| `POST /v1/auth/request` | `{email,deviceId}` | 202; send one-time login code. |
| `POST /v1/auth/verify` | `{email,deviceId,code}` | `{sessionToken,entitlement,serverTime}`. |
| `POST /v1/auth/signout` | Bearer | 204; revoke this server login. |
| `GET /v1/entitlement` | Bearer | Signed proof and trusted server time from current provider state. |
| `POST /v1/checkout` | Bearer | Disabled during public testing; future commercial phase returns `{url}` to Stripe hosted checkout. |
| `POST /v1/portal` | Bearer | `{url}` to Stripe customer portal. |
| `POST /v1/stripe/webhook` | Signed raw bytes | 200 after durable processing; retryable 503 on provider/storage failure. |
| `GET /v1/weather?city=...` | Authenticated Bearer; Premium additionally required in the commercial phase | `{location:{name,country,latitude,longitude},forecast:{...}}` when licensed provider configuration is ready. |

The desktop accepts only HTTPS links on `checkout.stripe.com` and `billing.stripe.com`. Public client configuration contains the billing base URI and RSA public PEM only. `SubscriptionService.TryCreateFromConfiguration` returns a clear disabled reason for empty/invalid settings. HTTP localhost is accepted for local test harnesses; production requires HTTPS. Login and proof state use `ISecretVault`, which maps to Credential Locker on Windows 10/11.

After configuring the service, copy `src/Notch.Windows/product-config.example.json` to `src/Notch.Windows/product-config.json` and supply its HTTPS origin and RSA **public** PEM. The project copies this public configuration into build/publish output and the installer. An absent configuration keeps billing disabled; the example alone never activates it. Do not put private keys or provider credentials in this file. Validate the configured signed candidate through the purchase/restore matrix before publishing.

Local sign-out removes the device's encrypted session and proof immediately. Applications should call `/v1/auth/signout` when online to revoke the server session as well; offline removal is immediate on this device, and server session expiry remains bounded to 30 days.

## Licensed weather proxy

Supply `Weather__ApiKey`, `Weather__ForecastBase` and `Weather__GeocodingBase` only after obtaining commercial access. The base URLs must use HTTPS and no embedded query/credentials. For a compatible Open-Meteo licensed deployment, the forecast base is the licensed customer host and geocoding base is the provider-approved host; confirm which endpoint accepts the licensed key with the provider.

The proxy calls `/v1/search` with `count=1&language=en&format=json`, and `/v1/forecast` with `timezone=auto`, `timeformat=unixtime`, seven-day daily weather and current/hourly fields. It exposes no shared provider key to customers. Requests require a current authenticated session; paid Premium state is additionally required in the commercial phase, while the explicit public-testing phase permits authenticated testers. Unconfigured weather returns 503; the desktop does not silently fall back to a non-commercial endpoint. The current app has no configured weather backend. Attribution remains required. The proxy uses a 15-second full-response deadline and a 1 MiB response limit. Production provider quota/cost monitoring and caching policy must match the commercial agreement.

## Local verification

```sh
dotnet build src/Notch.Billing/Notch.Billing.csproj
dotnet run --project tests/Notch.Commerce.Tests/Notch.Commerce.Tests.csproj
```

The commerce suite uses generated ephemeral keys, fake email, fake Stripe state, isolated temporary storage and a fake desktop HTTP handler. It checks signature/device/expiry boundaries, clock rollback, Free policy, OTP attempts and replay, failed-transaction preservation, purchase restore, bounded cancellation, duplicate/out-of-order webhooks, refund decisions, server sign-out and secure desktop caching. It makes **no live purchases, email deliveries or external provider calls**. Native Windows UX, real Stripe/SMTP configuration and deployed HTTPS are separate release checks.
