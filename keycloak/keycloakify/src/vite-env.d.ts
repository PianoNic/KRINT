/// <reference types="vite/client" />

import type { KcContext } from "./login/KcContext";

declare global {
    interface Window {
        kcContext?: KcContext;
    }
}

