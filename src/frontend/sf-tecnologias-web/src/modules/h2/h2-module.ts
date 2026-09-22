import React from "react";
import { H2Application } from "./components/h2-application";

export interface PlatformModule {
  id: string;
  name: string;
  component: (props?: any) => React.ReactNode;
  requiredPermissions?: string[];
}

export const h2ConvenienciaModule: PlatformModule = {
  id: "h2-conveniencia",
  name: "H2 Conveniência",
  component: (props?: any) => React.createElement(H2Application, props),
  requiredPermissions: [],
};

