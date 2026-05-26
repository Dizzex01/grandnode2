---
title: Spin the Wheel — Discount Coupon Game
date: 2026-05-10
status: approved
---

# Spin the Wheel — Design Spec

## Overview

A casino-style spin wheel page at `/spin` where logged-in customers spin to win a discount coupon code. Coupons are persisted to the database immediately on generation and auto-applied to the customer's cart, with a reconciliation step on cart load as a failsafe.

---

## Decisions

| Question | Decision |
|---|---|
| Who can spin? | Logged-in customers only (`[Authorize]`) |
| How often? | Configurable cooldown in hours (admin sets, e.g. 72 = once every 3 days) |
| Prize types | Discount coupon codes only |
| Page placement | Standalone page at `/spin`, linked from main nav |
| Segment config | Admin controls each segment: label, discount %, probability weight, color, coupon prefix |
| Post-spin UX | Coupon auto-applied to cart + code shown on screen with copy option |
| Wheel rendering | SVG `<path>` elements; no text on segments; hover tooltip shows label |

---

## Architecture

**One self-contained plugin:** `Promotion.SpinWheel`

Zero changes to `Grand.Core`, `Grand.Business`, or `Grand.Web` — the plugin hooks into existing extension points only.

```
src/Plugins/Promotion.SpinWheel/
├── SpinWheelPlugin.cs                  # IPlugin registration, route, nav link
├── Controllers/
│   └── SpinWheelController.cs          # GET /spin, POST /spin/execute
├── Areas/Admin/
│   └── Controllers/
│       └── SpinWheelConfigController.cs
│   └── Views/SpinWheel/
│       └── Configure.cshtml
├── Commands/
│   └── SpinWheelCommand.cs             # MediatR IRequest + IRequestHandler
├── Queries/
│   └── GetSpinStateQuery.cs            # Returns canSpin + nextSpinAt + segment config
├── Services/
│   └── SpinCouponReconciliationService.cs
├── Domain/
│   ├── SpinWheelSettings.cs
│   └── CustomerSpinRecord.cs
└── Views/SpinWheel/
    ├── Index.cshtml
    └── wwwroot/
        └── spinwheel.js               # Compiled Vue component, registered via plugin's bundled assets
```

### Data flow

```
Customer GET /spin
  → Auth middleware (redirect to login if guest)
  → GetSpinStateQuery → CustomerSpinRecord + SpinWheelSettings → { canSpin, nextSpinAt, segments[] }
  → Vue wheel renders (SVG segments from query response — label, color, weight per segment)

Customer clicks SPIN
  → POST /spin/execute
  → SpinWheelCommand handler:
      1. Cooldown gate
      2. Weighted random → winning segment
      3. IDiscountService → create Discount + DiscountCoupon
      4. Upsert CustomerSpinRecord.EarnedCoupons (AppliedToCart=false)
      5. ICartService.ApplyCoupon → flip AppliedToCart=true (non-critical)
      6. Return { segmentIndex, couponCode, discountLabel }
  → Vue animates wheel to segmentIndex
  → Result screen: coupon code + "Applied to your cart" message

Customer visits cart (any time after)
  → SpinCouponReconciliationService.EnsureApplied(customerId, cart)
      → Query CustomerSpinRecord for EarnedCoupons where AppliedToCart=false
      → ICartService.ApplyCoupon for each
      → Flip AppliedToCart=true on success
```

---

## Domain Model

### `SpinWheelSettings` (plugin settings, stored in PluginSettings collection)

```csharp
public class SpinWheelSettings : ISettings
{
    public bool Enabled { get; set; }
    public int CooldownHours { get; set; }          // e.g. 72
    public List<SpinSegment> Segments { get; set; } = new();
}

public class SpinSegment
{
    public string Id { get; set; }                  // unique, generated on create
    public string Label { get; set; }               // e.g. "20% Off"
    public decimal DiscountPercent { get; set; }    // e.g. 20.0
    public int ProbabilityWeight { get; set; }      // e.g. 20 (relative, not %)
    public string Color { get; set; }               // hex, e.g. "#0891b2"
    public string CouponPrefix { get; set; }        // e.g. "SPIN20"
}
```

Effective probability of a segment = `weight / sum(all weights)`. Weights do not need to sum to 100.

### `CustomerSpinRecord` (new MongoDB collection)

```csharp
public class CustomerSpinRecord : BaseEntity
{
    public string CustomerId { get; set; }
    public DateTime LastSpinUtc { get; set; }
    public int TotalSpins { get; set; }
    public List<SpinEarnedCoupon> EarnedCoupons { get; set; } = new();
}

public class SpinEarnedCoupon
{
    public string CouponCode { get; set; }          // e.g. "SPIN20-K7M2XQ"
    public string DiscountLabel { get; set; }       // e.g. "20% Off"
    public DateTime EarnedAtUtc { get; set; }
    public bool AppliedToCart { get; set; }
}
```

---

## Admin Configuration UI

Located at **Admin → Promotions → Spin Wheel → Configure**.

- **General settings:** `Enabled` toggle, `CooldownHours` input
- **Segments table:** one row per segment with inline-editable fields: color swatch, label, discount %, probability weight, coupon prefix, remove button
- **Add Segment** button appends a new row
- **Live weight summary** below the table shows effective probabilities (e.g. weight 20 of total 100 → 20%)
- **Save** persists `SpinWheelSettings` via existing plugin settings infrastructure

---

## Customer Page — `/spin`

### Wheel component (`SpinWheel.vue`)

- Registered as a Vue component via the plugin's bundled `wwwroot/spinwheel.js`, following the same pattern as existing theme components (compiled separately, referenced in `Index.cshtml`).
- Receives segment config as a JSON prop serialized from the `GetSpinStateQuery` response.
- Renders an SVG where each `<path>` is a pie-slice arc computed from:
  - `angle = (weight / totalWeight) × 360`
  - `x = cx + r·sin(θ)`, `y = cy − r·cos(θ)`
- **No text on segments.** Hover triggers `@mouseenter`/`@mouseleave` — active segment glows (SVG `filter: drop-shadow`), others dim to 40% opacity, a tooltip div appears with the segment label.
- Tooltip is suppressed during spin animation.
- Spin animation: CSS `transform: rotate()` transition, duration ~3s, easing `cubic-bezier(0.17, 0.67, 0.12, 0.99)`, lands on the server-determined `segmentIndex`.
- A fixed pointer arrow (`▼`) sits above the wheel center.

### Page states

**State A — Ready**
- Wheel rendered, SPIN NOW button active
- If cooldown active: button disabled, countdown shown (`Next spin in: Xd Yh`)

**State B — Spinning**
- Button disabled, wheel animates 5–8 full rotations then lands on winning segment

**State C — Result**
- Confetti burst
- Won label ("You won 20% Off!")
- Coupon code in a styled box with copy-to-clipboard button
- "Applied to your cart" confirmation badge
- CTAs: Go to Cart / Continue Shopping
- Cooldown countdown starts

---

## Backend — Spin Logic (`SpinWheelCommand`)

```
1. Load CustomerSpinRecord for customer
2. If LastSpinUtc + CooldownHours > UtcNow → return { canSpin: false, nextSpinAt }
3. Sum all segment ProbabilityWeights → totalWeight
4. draw = Random.Shared.Next(0, totalWeight)
5. Walk segments accumulating weights; stop when running total > draw → winningSegment
6. code = $"{winningSegment.CouponPrefix}-{GenerateAlphanumeric(6)}"
7. IDiscountService.CreateDiscount(percentage = winningSegment.DiscountPercent, singleUse = true)
8. IDiscountService.CreateCoupon(discountId, code, customerId)
9. Upsert CustomerSpinRecord: EarnedCoupons.Add({ code, label, EarnedAtUtc=UtcNow, AppliedToCart=false })
                              LastSpinUtc=UtcNow, TotalSpins++
10. ICartService.ApplyCoupon(customerId, code) → on success, flip AppliedToCart=true
11. Return { canSpin: true, segmentIndex, couponCode: code, discountLabel }
```

Step 10 is non-critical — failure does not abort the command. The coupon is safe in the DB from step 9.

---

## Failsafe — Cart Load Reconciliation

`SpinCouponReconciliationService.EnsureApplied(customerId, cart)` is called whenever the customer's cart is loaded.

```
1. Query CustomerSpinRecord for customerId
2. Filter EarnedCoupons where AppliedToCart = false
3. For each: ICartService.ApplyCoupon(customerId, couponCode)
             → on success: flip AppliedToCart = true and save
4. No error surfaced to UI — silent reconciliation
```

**Fallback chain:**

| Scenario | Outcome |
|---|---|
| Cart apply fails at spin time | Coupon in DB; reconciliation applies on next cart load |
| User closes browser before seeing result | Coupon in DB; applied on next cart load |
| Reconciliation also fails | Coupon code shown on spin result screen (copy-to-clipboard); customer pastes at checkout |
| Duplicate apply attempt | `IDiscountService` deduplicates coupons per cart — idempotent |
| No segments configured | `SpinWheelCommand` returns 400; SPIN NOW button disabled via `GetSpinStateQuery` |
| Customer not logged in | `[Authorize]` on controller redirects to login |

---

## Testing

| Layer | What to test |
|---|---|
| `SpinWheelCommand` unit | Weighted random distribution over many draws matches expected probabilities |
| `SpinWheelCommand` unit | Cooldown gate blocks spin when within cooldown window |
| `SpinWheelCommand` unit | Coupon code format matches `{Prefix}-{6 alphanumeric}` |
| `SpinWheelCommand` unit | `CustomerSpinRecord` upserted correctly; `AppliedToCart` starts false |
| `SpinCouponReconciliationService` unit | Unapplied coupons are applied; `AppliedToCart` flipped on success |
| `SpinCouponReconciliationService` unit | Already-applied coupons are not re-attempted |
| Integration | Full spin flow: spin → coupon in DB → cart load → coupon in cart |
| Integration | Cooldown: second spin within window returns `canSpin: false` |
| Admin UI | Save settings → reload → segments and cooldown persisted correctly |
