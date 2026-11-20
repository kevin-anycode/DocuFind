import React from "react";
import { Composition } from "remotion";
import { Promo } from "./Promo";

export const RemotionRoot: React.FC = () => {
  return (
    <Composition
      id="DocuFindPromo"
      component={Promo}
      durationInFrames={600}
      fps={30}
      width={1920}
      height={1080}
    />
  );
};
