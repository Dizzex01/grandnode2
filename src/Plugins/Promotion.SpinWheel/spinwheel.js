Vue.component('spin-wheel', {
    props: {
        segments:     { type: Array,   required: true },
        canSpin:      { type: Boolean, default: true },
        nextSpinAt:   { type: String,  default: null },
        executeUrl:   { type: String,  required: true },
        cartUrl:      { type: String,  required: true }
    },
    data() {
        return {
            spinning: false,
            result: null,
            rotation: 0,
            hoveredIndex: null,
            countdown: '',
            cx: 150, cy: 150, r: 140
        };
    },
    computed: {
        totalWeight() {
            return this.segments.reduce((sum, s) => sum + s.probabilityWeight, 0);
        },
        paths() {
            let startAngle = 0;
            return this.segments.map((seg, i) => {
                const angle = (seg.probabilityWeight / this.totalWeight) * 360;
                const path = this.describeArc(startAngle, startAngle + angle);
                const midAngle = startAngle + angle / 2;
                startAngle += angle;
                return { ...seg, path, midAngle, index: i };
            });
        }
    },
    mounted() {
        if (!this.canSpin && this.nextSpinAt)
            this.startCountdown(new Date(this.nextSpinAt));
    },
    methods: {
        toRad(deg) { return deg * Math.PI / 180; },
        pointOnCircle(angleDeg) {
            const rad = this.toRad(angleDeg - 90);
            return {
                x: this.cx + this.r * Math.cos(rad),
                y: this.cy + this.r * Math.sin(rad)
            };
        },
        describeArc(startDeg, endDeg) {
            const s = this.pointOnCircle(startDeg);
            const e = this.pointOnCircle(endDeg);
            const large = (endDeg - startDeg) > 180 ? 1 : 0;
            return `M${this.cx},${this.cy} L${s.x.toFixed(2)},${s.y.toFixed(2)} A${this.r},${this.r} 0 ${large},1 ${e.x.toFixed(2)},${e.y.toFixed(2)} Z`;
        },
        segmentStyle(index) {
            if (this.spinning) return {};
            if (this.hoveredIndex === null) return {};
            return this.hoveredIndex === index
                ? { filter: 'drop-shadow(0 0 8px rgba(0,0,0,0.6))' }
                : { opacity: '0.4' };
        },
        async spin() {
            if (this.spinning || !this.canSpin) return;
            this.spinning = true;
            this.result = null;

            console.log('Spinning the wheel...');

            const token = document.querySelector('input[name="__RequestVerificationToken"]');

            try {
                const resp = await fetch(this.executeUrl, {
                    method: 'POST',
                    headers: token ? { 'RequestVerificationToken': token.value } : {}
                });
                const data = await resp.json();

                if (!data.Success) {
                    alert(data.ErrorMessage || 'Could not spin. Please try again.');
                    this.spinning = false;
                    return;
                }

                // Compute rotation to land pointer (top) on the winning segment's midpoint
                let startAngle = 0;
                for (let i = 0; i < data.WinningSegmentIndex; i++)
                    startAngle += (this.segments[i].probabilityWeight / this.totalWeight) * 360;
                const segAngle = (this.segments[data.WinningSegmentIndex].probabilityWeight / this.totalWeight) * 360;
                const midAngle = startAngle + segAngle / 2;
                const extraSpins = 6;
                this.rotation += (360 * extraSpins) + (360 - midAngle) - (this.rotation % 360);

                setTimeout(() => {
                    this.result = data;
                    this.spinning = false;
                    this.startCountdown(new Date(data.NextSpinAt));
                }, 3200);

            } catch (e) {
                alert('An error occurred. Please refresh and try again.');
                this.spinning = false;
            }
        },
        startCountdown(until) {
            const tick = () => {
                const diff = until - new Date();
                if (diff <= 0) { this.countdown = ''; return; }
                const h = Math.floor(diff / 3600000);
                const m = Math.floor((diff % 3600000) / 60000);
                this.countdown = `${h}h ${m}m`;
                setTimeout(tick, 60000);
            };
            tick();
        },
        copyCode() {
            navigator.clipboard.writeText(this.result.CouponCode)
                .then(() => alert('Copied!'))
                .catch(() => {});
        }
    },
    template: `
    <div style="text-align:center;padding:40px 20px;max-width:520px;margin:0 auto;">
        <h1 style="margin-bottom:4px;">Spin to Win!</h1>
        <p style="color:#888;margin-bottom:24px;">
            Spin the wheel for a discount coupon.
            <span v-if="countdown"> Next spin in: <strong>{{ countdown }}</strong></span>
        </p>

        <!-- Result screen -->
        <div v-if="result" style="margin-bottom:32px;">
            <div style="font-size:48px;">&#127881;</div>
            <h2 style="color:#059669;">You won {{ result.DiscountLabel }}!</h2>
            <p style="color:#888;">Applied to your cart automatically.</p>
            <div style="border:2px dashed #059669;border-radius:8px;padding:16px;margin:16px auto;max-width:280px;background:#f0fdf4;">
                <div style="font-size:11px;color:#888;text-transform:uppercase;letter-spacing:1px;">Your Coupon Code</div>
                <div style="font-size:22px;font-weight:bold;font-family:monospace;letter-spacing:3px;color:#059669;margin:6px 0;">
                    {{ result.CouponCode }}
                </div>
                <button @click="copyCode" style="font-size:12px;background:none;border:1px solid #059669;color:#059669;padding:4px 12px;border-radius:4px;cursor:pointer;">
                    Copy Code
                </button>
            </div>
            <div style="display:flex;gap:12px;justify-content:center;margin-top:16px;">
                <a :href="cartUrl" style="padding:10px 20px;background:#7c3aed;color:#fff;border-radius:6px;text-decoration:none;">Go to Cart</a>
                <a href="/" style="padding:10px 20px;border:1px solid #ccc;border-radius:6px;color:#555;text-decoration:none;">Continue Shopping</a>
            </div>
        </div>

        <!-- Wheel -->
        <div style="position:relative;display:inline-block;">
            <div style="position:absolute;top:-22px;left:50%;transform:translateX(-50%);font-size:24px;z-index:10;line-height:1;">&#9660;</div>
            <svg :width="cx*2" :height="cy*2"
                 :style="{ transition: spinning ? 'transform 3.2s cubic-bezier(0.17,0.67,0.12,0.99)' : 'none', transform: 'rotate(' + rotation + 'deg)', transformOrigin: cx+'px '+cy+'px' }">
                <defs>
                    <filter id="sw-glow" x="-30%" y="-30%" width="160%" height="160%">
                        <feGaussianBlur stdDeviation="4" result="blur"/>
                        <feMerge><feMergeNode in="blur"/><feMergeNode in="SourceGraphic"/></feMerge>
                    </filter>
                </defs>
                <path v-for="seg in paths" :key="seg.index"
                      :d="seg.path"
                      :fill="seg.color"
                      stroke="#fff" stroke-width="2"
                      :style="segmentStyle(seg.index)"
                      style="cursor:pointer;transition:opacity 0.2s,filter 0.2s;"
                      @mouseenter="!spinning && (hoveredIndex = seg.index)"
                      @mouseleave="hoveredIndex = null">
                </path>
                <circle :cx="cx" :cy="cy" r="20" fill="#fff" stroke="#333" stroke-width="3"/>
            </svg>
            <!-- Hover tooltip (rendered outside SVG so it can overflow) -->
            <div v-if="hoveredIndex !== null && !spinning"
                 style="position:absolute;background:#1e293b;color:#fff;border-radius:6px;padding:6px 12px;font-size:13px;pointer-events:none;white-space:nowrap;z-index:20;bottom:calc(100% + 8px);left:50%;transform:translateX(-50%);">
                {{ segments[hoveredIndex].label }}
            </div>
        </div>

        <div style="margin-top:24px;">
            <button @click="spin"
                    :disabled="spinning || !canSpin || !!result"
                    :style="{ padding: '12px 36px', fontSize: '16px', background: '#7c3aed', color: '#fff', border: 'none', borderRadius: '8px', cursor: (spinning || !canSpin || !!result) ? 'not-allowed' : 'pointer', opacity: (spinning || !canSpin || !!result) ? 0.5 : 1 }">
                {{ spinning ? 'Spinning...' : 'SPIN NOW' }}
            </button>
        </div>
    </div>`
});
