import React from "react";
import {
  AbsoluteFill,
  Img,
  interpolate,
  spring,
  staticFile,
  useCurrentFrame,
  useVideoConfig,
  Easing,
} from "remotion";

const BG_TOP = "#0B0E11";
const BG_BOTTOM = "#141A20";
const ACCENT = "#4CC2FF";
const TEXT = "#F2F4F6";
const MUTED = "#98A2AC";
const FONT = '"Microsoft YaHei UI", "Segoe UI", sans-serif';

const clamp = { extrapolateLeft: "clamp", extrapolateRight: "clamp" } as const;

const fadeUp = (frame: number, start: number, duration = 18, distance = 26) => {
  const p = interpolate(frame, [start, start + duration], [0, 1], {
    ...clamp,
    easing: Easing.out(Easing.cubic),
  });
  return { opacity: p, transform: `translateY(${(1 - p) * distance}px)` };
};

const Backdrop: React.FC = () => (
  <AbsoluteFill style={{ background: `linear-gradient(160deg, ${BG_TOP} 0%, ${BG_BOTTOM} 100%)` }}>
    <AbsoluteFill
      style={{
        background:
          "radial-gradient(1100px 620px at 72% 38%, rgba(76,194,255,0.13), rgba(0,0,0,0) 68%)",
      }}
    />
    <AbsoluteFill
      style={{
        background:
          "radial-gradient(900px 520px at 16% 84%, rgba(76,194,255,0.06), rgba(0,0,0,0) 70%)",
      }}
    />
  </AbsoluteFill>
);

const Caption: React.FC<{
  frame: number;
  start: number;
  text: string;
  size?: number;
  color?: string;
  weight?: number;
  align?: "left" | "center";
}> = ({ frame, start, text, size = 44, color = TEXT, weight = 600, align = "center" }) => (
  <div
    style={{
      ...fadeUp(frame, start, 20, 18),
      fontFamily: FONT,
      fontSize: size,
      fontWeight: weight,
      color,
      textAlign: align,
      letterSpacing: 1,
    }}
  >
    {text}
  </div>
);

const SceneIntro: React.FC = () => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();

  const title = spring({ frame: frame - 4, fps, config: { damping: 200 }, durationInFrames: 34 });
  const rule = interpolate(frame, [30, 62], [0, 230], clamp);

  return (
    <AbsoluteFill style={{ alignItems: "center", justifyContent: "center" }}>
      <div
        style={{
          transform: `translateY(${(1 - title) * 34}px)`,
          opacity: title,
          fontFamily: FONT,
          fontSize: 132,
          fontWeight: 700,
          color: TEXT,
          letterSpacing: -2,
        }}
      >
        DocuFind
      </div>
      <div style={{ height: 3, width: rule, background: ACCENT, margin: "26px 0 26px", borderRadius: 2 }} />
      <div style={{ ...fadeUp(frame, 22, 22, 20), fontFamily: FONT, fontSize: 46, color: TEXT, letterSpacing: 10 }}>
        文档秒查
      </div>
      <div
        style={{
          ...fadeUp(frame, 34, 22, 18),
          marginTop: 28,
          fontFamily: FONT,
          fontSize: 30,
          color: MUTED,
          letterSpacing: 2,
        }}
      >
        住在任务栏里的文档搜索
      </div>
    </AbsoluteFill>
  );
};

const SceneTaskbar: React.FC = () => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();

  const rise = spring({ frame, fps, config: { damping: 22, mass: 0.9 }, durationInFrames: 42 });
  const glow = interpolate(frame, [38, 88], [0, 1], clamp);

  return (
    <AbsoluteFill>
      <div style={{ position: "absolute", top: 196, left: 0, right: 0, display: "flex", justifyContent: "center" }}>
        <Caption frame={frame} start={10} text="把文档拖进去，它就一直在任务栏里" size={42} />
      </div>

      <div style={{ position: "absolute", left: 0, right: 0, top: 540, display: "flex", justifyContent: "center" }}>
        <div
          style={{
            position: "relative",
            width: 1320,
            transform: `translateY(${(1 - rise) * 420}px)`,
            opacity: rise,
            filter: "drop-shadow(0 30px 60px rgba(0,0,0,0.55))",
          }}
        >
          <div
            style={{
              position: "absolute",
              left: 960,
              top: -70,
              width: 400,
              height: 230,
              background: `radial-gradient(closest-side, rgba(76,194,255,${0.3 * glow}), rgba(0,0,0,0))`,
            }}
          />
          <Img
            src={staticFile("taskbar.png")}
            style={{ width: 1320, height: 69, display: "block", borderRadius: 10 }}
          />
        </div>
      </div>
    </AbsoluteFill>
  );
};

const SceneResults: React.FC = () => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();

  const enter = spring({ frame, fps, config: { damping: 20, mass: 1.1 }, durationInFrames: 46 });
  const push = interpolate(frame, [55, 130], [0, 1], { ...clamp, easing: Easing.inOut(Easing.quad) });

  return (
    <AbsoluteFill>
      <div style={{ position: "absolute", top: 92, left: 170, right: 0 }}>
        <Caption frame={frame} start={8} text="记得内容，不记得文件名？" size={46} align="left" />
      </div>

      <div style={{ position: "absolute", left: 0, right: 0, top: 214, display: "flex", justifyContent: "center" }}>
        <div
          style={{
            transform: `translateY(${(1 - enter) * 130}px) scale(${0.94 + enter * 0.06 + push * 0.045})`,
            opacity: enter,
            filter: "drop-shadow(0 40px 90px rgba(0,0,0,0.55))",
          }}
        >
          <Img src={staticFile("panel.png")} style={{ width: 900, height: 660, display: "block", borderRadius: 12 }} />
        </div>
      </div>

      <div style={{ position: "absolute", bottom: 66, left: 0, right: 0, display: "flex", justifyContent: "center" }}>
        <Caption frame={frame} start={26} text="直接给到命中的那一段，还有页码" size={30} color={MUTED} weight={400} />
      </div>
    </AbsoluteFill>
  );
};

const ScenePoints: React.FC = () => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();

  const points = [
    { title: "搜正文", desc: "不只是文件名" },
    { title: "懂中文", desc: "换个说法也能中" },
    { title: "全本地", desc: "不联网、不上传" },
  ];

  return (
    <AbsoluteFill style={{ alignItems: "center", justifyContent: "center" }}>
      <div style={{ display: "flex", gap: 44 }}>
        {points.map((p, i) => {
          const s = spring({ frame: frame - i * 10, fps, config: { damping: 200 }, durationInFrames: 30 });
          return (
            <div
              key={p.title}
              style={{
                width: 430,
                height: 300,
                borderRadius: 22,
                background: "rgba(255,255,255,0.045)",
                border: "1px solid rgba(255,255,255,0.10)",
                display: "flex",
                flexDirection: "column",
                alignItems: "center",
                justifyContent: "center",
                gap: 18,
                opacity: s,
                transform: `translateY(${(1 - s) * 40}px)`,
                fontFamily: FONT,
              }}
            >
              <div style={{ fontSize: 54, fontWeight: 700, color: TEXT, letterSpacing: 2 }}>{p.title}</div>
              <div style={{ fontSize: 26, color: ACCENT, letterSpacing: 1 }}>{p.desc}</div>
            </div>
          );
        })}
      </div>
    </AbsoluteFill>
  );
};

const SceneOutro: React.FC = () => {
  const frame = useCurrentFrame();

  return (
    <AbsoluteFill style={{ alignItems: "center", justifyContent: "center" }}>
      <div style={{ ...fadeUp(frame, 6, 26, 24), fontFamily: FONT, fontSize: 76, fontWeight: 700, color: TEXT, letterSpacing: -1 }}>
        DocuFind <span style={{ color: ACCENT }}>·</span> 文档秒查
      </div>
      <div style={{ ...fadeUp(frame, 22, 24, 18), marginTop: 30, fontFamily: FONT, fontSize: 34, color: MUTED, letterSpacing: 1 }}>
        把文档拖进任务栏，之后一句话搜到里面任何内容
      </div>
      <div style={{ ...fadeUp(frame, 44, 24, 16), marginTop: 58, fontFamily: FONT, fontSize: 28, color: "#7CC7EE", letterSpacing: 1 }}>
        gitee.com/kevin-anycode/DocuFind
      </div>
      <div style={{ ...fadeUp(frame, 54, 24, 14), marginTop: 18, fontFamily: FONT, fontSize: 22, color: "#6E7883", letterSpacing: 2 }}>
        WINDOWS 11 · 本地运行 · 无需联网
      </div>
    </AbsoluteFill>
  );
};

export const Promo: React.FC = () => {
  const frame = useCurrentFrame();

  return (
    <AbsoluteFill style={{ backgroundColor: BG_TOP }}>
      <Backdrop />
      {frame < 90 && <SceneIntro />}
      {frame >= 90 && frame < 210 && <SceneTaskbar />}
      {frame >= 210 && frame < 390 && <SceneResults />}
      {frame >= 390 && frame < 480 && <ScenePoints />}
      {frame >= 480 && <SceneOutro />}
    </AbsoluteFill>
  );
};
