// ゴールに得点が入ったときの光（SpaceJunkGoalScoreEffect）。説明は Documents/機能ドキュメント/ゴールの得点の演出.md
// 1つのシェーダーで3つの部品を描く。どれを描くかは、スクリプトが MaterialPropertyBlock の _Part で渡す。
//   0 = 光の壁 … ゴールのまわりを囲む四角い筒。下ほど明るく、上へ向かって消える。縦の筋が上へ流れる
//   1 = 光の輪 … ゴールの床の高さで、ゴールのふちから外へ広がる四角い輪（角は広がるほど丸くなる）
//   2 = 光の粒 … ゴールのふちから立ちのぼる小さな光の粒（カメラのほうを向く板）
// 色はチームの色（_Color）。光る分を足しつつ、下の床を少し暗くする重ね方（Blend One OneMinusSrcAlpha）なので、明るい床の上でもチームの色が白に飛びにくい。
Shader "StarSweepers/GoalScoreGlow"
{
    Properties
    {
        _Color ("Color (チームの色。スクリプトが入れる)", Color) = (0.3, 0.6, 0.95, 1)
        _Intensity ("Intensity (明るさ。スクリプトが時間で変える)", Range(0, 4)) = 1
        _Progress ("Progress (進み具合 0-1。スクリプトが入れる)", Range(0, 1)) = 0.3
        _Part ("Part (0=壁 1=輪 2=粒。スクリプトが入れる)", Float) = 0
        _Size ("Size (x,y=ゴールの幅と奥行き m、z=輪の広がり m、w=輪の半径 m。スクリプトが入れる)", Vector) = (4, 4, 2, 1)
        _Spark ("Spark (x=のぼる高さ m、y=粒の大きさ m、z=外へ広がる距離 m。スクリプトが入れる)", Vector) = (3, 0.12, 0.6, 0)
        _WhiteCore ("White Core (光のいちばん明るい所を白に寄せる量)", Range(0, 1)) = 0.3
        _Streaks ("Streaks (壁の縦の筋の濃さ)", Range(0, 1)) = 0.6
        _RingWidth ("Ring Width (輪の太さ m)", Range(0.05, 1.5)) = 0.45
        _EdgeGlow ("Edge Glow (ゴールのふちの外側の床のにじみ)", Range(0, 2)) = 0.8
        _Cover ("Cover (光の下の床を暗くする強さ。大きいほどチームの色がはっきりする)", Range(0, 1)) = 0.55
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "GoalScoreGlow"
            Tags { "LightMode" = "UniversalForward" }

            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Intensity;
                float _Progress;
                float _Part;
                float4 _Size;
                float4 _Spark;
                float _WhiteCore;
                float _Streaks;
                float _RingWidth;
                float _EdgeGlow;
                float _Cover;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;   // 壁：y=高さ 0-1／粒：板の角 -1〜1
                float2 seed : TEXCOORD1; // 粒：ばらつき用の乱数 0-1
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 local : TEXCOORD1; // 壁：横の位置 m／輪：真ん中からの位置 m
                float fade : TEXCOORD2;   // 粒：消え具合
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.uv = input.uv;
                output.local = 0;
                output.fade = 0;

                if (_Part > 1.5)
                {
                    // ---- 光の粒：ふちの決まった場所から、時間差でのぼって消える ----
                    float delay = input.seed.x * 0.4;
                    float life = 0.45 + input.seed.y * 0.55;
                    float t = (_Progress - delay) / life;
                    float alive = step(0.0, t) * step(t, 1.0);
                    t = saturate(t);

                    float3 root = TransformObjectToWorld(input.positionOS.xyz);
                    float3 middle = TransformObjectToWorld(float3(0, 0, 0));
                    float2 outward = root.xz - middle.xz;
                    outward = outward / max(length(outward), 0.001);

                    float ease = 1.0 - (1.0 - t) * (1.0 - t);
                    float3 world = root;
                    world.y += _Spark.x * (0.45 + input.seed.y * 0.75) * ease;
                    world.xz += outward * _Spark.z * input.seed.x * ease;

                    float size = _Spark.y * (0.6 + input.seed.x * 0.8) * (1.0 - t * 0.7) * alive;
                    float3 view = TransformWorldToView(world);
                    view.xy += input.uv * size;
                    output.positionHCS = TransformWViewToHClip(view);
                    output.fade = (1.0 - t) * alive;
                    return output;
                }

                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                if (_Part > 0.5)
                {
                    // 輪の板は、ゴールの幅＋広がりの2倍の大きさ。真ん中からの位置を m で渡す
                    output.local = input.positionOS.xz * (_Size.xy + 2.0 * _Size.z);
                }
                else
                {
                    // 壁は、ふちに沿った横の位置を m で渡す（縦の筋に使う）
                    output.local = float2(input.positionOS.x * _Size.x + input.positionOS.z * _Size.y, 0);
                }
                return output;
            }

            // 角を丸めた四角からの距離（外が正、中が負）
            float RoundBoxDistance(float2 p, float2 halfSize, float radius)
            {
                float2 q = abs(p) - halfSize + radius;
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - radius;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half3 tint = _Color.rgb;
                float light = 0;
                float core = 0;

                if (_Part > 1.5)
                {
                    // 光の粒：まるいぼかし。真ん中は白っぽく
                    float r = length(input.uv);
                    float blob = saturate(1.0 - r);
                    light = blob * blob * input.fade * 1.6;
                    core = saturate(1.0 - r * 2.2);
                }
                else if (_Part > 0.5)
                {
                    // 光の輪：ふちから外へ広がる輪と、ふちのすぐ外の床のにじみ
                    float radius = max(_Size.w, 0.0);
                    float d = RoundBoxDistance(input.local, _Size.xy * 0.5, min(radius * 0.6, min(_Size.x, _Size.y) * 0.5));
                    float w = max(_RingWidth, 0.01);
                    float ring = exp(-((d - radius) * (d - radius)) / (w * w * 0.5)) * step(-0.02, d);
                    float edge = exp(-max(d, 0.0) / 0.35) * step(0.0, d) * _EdgeGlow;
                    // 板のふちで必ず消す（四角い板の形が見えないように）
                    float outer = _Size.z + w;
                    float limit = 1.0 - smoothstep(outer * 0.8, outer, d);
                    light = max(ring, edge * (1.0 - _Progress)) * limit;
                    core = ring * 0.8;
                }
                else
                {
                    // 光の壁：下ほど明るく、上で消える。下のふちは白く光る線、縦の筋が上へ流れる
                    float v = saturate(input.uv.y);
                    float body = pow(1.0 - v, 1.7);
                    float rim = exp(-v * 22.0);
                    float h = input.local.x;
                    float stripe = 0.5 + 0.5 * sin(h * 6.3 + sin(h * 1.7) * 2.5);
                    float rise = frac(v * 1.3 - _Progress * 2.2 + sin(h * 2.1) * 0.15);
                    float flow = smoothstep(0.0, 0.15, rise) * (1.0 - smoothstep(0.35, 0.6, rise));
                    float streak = lerp(1.0, stripe * (0.55 + flow), _Streaks);
                    light = body * streak + rim * 1.2;
                    light *= 1.0 - smoothstep(0.85, 1.0, v);
                    core = rim;
                }

                half3 color = lerp(tint, half3(1, 1, 1), saturate(core * _WhiteCore));
                float amount = light * _Intensity;
                return half4(color * amount, saturate(amount * _Cover));
            }
            ENDHLSL
        }
    }
}
