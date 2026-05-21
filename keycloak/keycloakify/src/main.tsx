import { createRoot } from "react-dom/client";
import { StrictMode } from "react";
import "./login/index.css";
import { KcPage } from "./kc.gen";

// Follow the browser's prefers-color-scheme — no in-app toggle.
const applySystemTheme = () => {
    const isDark = window.matchMedia("(prefers-color-scheme: dark)").matches;
    document.documentElement.classList.toggle("dark", isDark);
    document.documentElement.classList.toggle("light", !isDark);
};
applySystemTheme();
window
    .matchMedia("(prefers-color-scheme: dark)")
    .addEventListener("change", applySystemTheme);

createRoot(document.getElementById("root")!).render(
    <StrictMode>
        {!window.kcContext ? (
            <h1>No Keycloak Context</h1>
        ) : (
            <KcPage kcContext={window.kcContext} />
        )}
    </StrictMode>
);
