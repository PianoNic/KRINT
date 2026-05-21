import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from '@/components/ui/tooltip';
import { kcSanitize } from "@keycloakify/login-ui/kcSanitize";
import { useKcClsx } from "@keycloakify/login-ui/useKcClsx";
import { useSetClassName } from "keycloakify/tools/useSetClassName";
import { RotateCcw, User } from "lucide-react";
import type { ReactNode } from "react";
import { useEffect } from "react";
import { useI18n } from "../../i18n";
import { useKcContext } from "../../KcContext";
import { Languages } from '../ui/Langauges';
import companylogo from "./../../assets/img/auth-logo.svg";
import shape from "./../../assets/img/shape.svg";
import { useInitializeTemplate } from "./useInitializeTemplate";

const APP_NAME = "KRINT";

export function Template(props: {
    displayInfo?: boolean;
    displayMessage?: boolean;
    displayRequiredFields?: boolean;
    headerNode: ReactNode;
    socialProvidersNode?: ReactNode;
    infoNode?: ReactNode;
    documentTitle?: string;
    bodyClassName?: string;
    children: ReactNode;
}) {
    const {
        displayInfo = false,
        displayMessage = true,
        displayRequiredFields = false,
        headerNode,
        socialProvidersNode = null,
        infoNode = null,
        documentTitle,
        bodyClassName,
        children
    } = props;

    const { kcContext } = useKcContext();

    const { auth, url, message, isAppInitiatedAction } = kcContext;

    const { msg, msgStr, enabledLanguages } = useI18n();

    const { kcClsx } = useKcClsx();

    useEffect(() => {
        document.title =
            documentTitle ??
            msgStr("loginTitle", kcContext.realm.displayName || kcContext.realm.name);
    }, []);

    useSetClassName({
        qualifiedName: "html",
        className: kcClsx("kcHtmlClass")
    });

    useSetClassName({
        qualifiedName: "body",
        className: bodyClassName ?? kcClsx("kcBodyClass")
    });

    useInitializeTemplate();

    return (
        <div className="grid min-h-svh lg:grid-cols-[40%_60%]">
            {/* Branding panel — left on desktop, hidden on mobile */}
            <div className="bg-primary relative hidden lg:block dark:bg-white/5 order-1">
                <div className="absolute right-0 top-0 w-full max-w-62.5 xl:max-w-112.5">
                    <img src={shape} alt="grid" />
                </div>
                <div className="absolute bottom-0 left-0 w-full max-w-62.5 rotate-180 xl:max-w-112.5">
                    <img src={shape} alt="grid" />
                </div>

                <div className="relative z-10 flex h-full w-full flex-col items-center justify-center px-8 text-center">
                    <div className="mb-4 flex items-center justify-center gap-3">
                        <img src={companylogo} alt="Logo" className="h-12 w-12" />
                        <span className="text-white text-2xl font-semibold">{APP_NAME}</span>
                    </div>

                    <p className="max-w-sm text-gray-400">
                        {msg("welcomeMessage")}
                    </p>
                </div>
            </div>

            {/* Main content (form) — right on desktop */}
            <div className="flex flex-col gap-4 px-4 py-0 pb-6 lg:p-6 lg:md:p-10 lg:pt-10 min-h-screen lg:min-h-0 order-2">
                {enabledLanguages.length > 1 && (
                    <div className="absolute top-4 inset-s-4 z-20 flex gap-2">
                        <Languages />
                    </div>
                )}

                <div className="flex flex-1 items-center justify-center flex-col">
                    <div className="w-full max-w-md mx-auto">

                        <Card className="shadow-none bg-transparent lg:bg-card border-0 lg:rounded-lg lg:border lg:shadow-sm rounded-t-2xl">
                            <CardHeader>
                                <CardTitle>
                                    {/* Mobile header with logo */}
                                    <div className="lg:hidden relative mt-8">
                                        <div className="mb-6 flex items-center justify-center gap-3 text-center">
                                            <img src={companylogo} alt="Logo" className="h-10 w-10" />
                                            <span className="text-2xl font-semibold">{APP_NAME}</span>
                                        </div>
                                    </div>
                                    {(() => {
                                        const node = !(
                                            auth !== undefined &&
                                            auth.showUsername &&
                                            !auth.showResetCredentials
                                        ) ? (
                                            <h1 className="text-xl">{headerNode}</h1>
                                        ) : (
                                            <div
                                                id="kc-username"
                                                className="flex items-center justify-between gap-2"
                                            >
                                                <div className="flex gap-4 items-center">
                                                    <User className="text-muted-foreground size-6" />

                                                    <div className="flex flex-col gap-0.5">
                                                        <span className="text-xs font-normal text-muted-foreground">
                                                            {msgStr("attemptedUsernameLoggingInAs")}
                                                        </span>
                                                        <label
                                                            className="font-semibold text-lg"
                                                            id="kc-attempted-username"
                                                        >
                                                            {auth.attemptedUsername}
                                                        </label>
                                                    </div>
                                                </div>

                                                <TooltipProvider>
                                                    <Tooltip>
                                                        <TooltipTrigger asChild>
                                                            <Button
                                                                variant="outline"
                                                                size="icon"
                                                                asChild
                                                            >
                                                                <a
                                                                    id="reset-login"
                                                                    href={
                                                                        url.loginRestartFlowUrl
                                                                    }
                                                                    aria-label={msgStr(
                                                                        "restartLoginTooltip"
                                                                    )}
                                                                >
                                                                    <RotateCcw className="size-4" />
                                                                </a>
                                                            </Button>
                                                        </TooltipTrigger>
                                                        <TooltipContent>
                                                            <p>
                                                                {msg(
                                                                    "restartLoginTooltip"
                                                                )}
                                                            </p>
                                                        </TooltipContent>
                                                    </Tooltip>
                                                </TooltipProvider>
                                            </div>
                                        );

                                        if (displayRequiredFields) {
                                            return (
                                                <div className="flex items-center justify-between gap-2">
                                                    <div>{node}</div>
                                                    <div>
                                                        <span className="subtitle">
                                                            <span className="text-red-500">
                                                                *
                                                            </span>
                                                            {msg("requiredFields")}
                                                        </span>
                                                    </div>
                                                </div>
                                            );
                                        }

                                        return node;
                                    })()}
                                </CardTitle>

                            </CardHeader>
                            <CardContent>
                                <div id="kc-content" className="space-y-4">
                                    {displayMessage &&
                                        message !== undefined &&
                                        (message.type !== "warning" ||
                                            !isAppInitiatedAction) && (
                                            <Alert variant={message.type}>
                                                <AlertDescription>
                                                    <span
                                                        dangerouslySetInnerHTML={{
                                                            __html: kcSanitize(
                                                                message.summary
                                                            )
                                                        }}
                                                    />
                                                </AlertDescription>
                                            </Alert>
                                        )}
                                    {socialProvidersNode}
                                    {children}
                                    {auth !== undefined &&
                                        auth.showTryAnotherWayLink && (
                                            <form
                                                id="kc-select-try-another-way-form"
                                                action={url.loginAction}
                                                method="post"
                                            >
                                                <div
                                                    className={kcClsx(
                                                        "kcFormGroupClass"
                                                    )}
                                                >
                                                    <input
                                                        type="hidden"
                                                        name="tryAnotherWay"
                                                        value="on"
                                                    />

                                                    <Button type="button" className='w-full' variant="outline" asChild>
                                                        <a
                                                            href="#"
                                                            id="try-another-way"
                                                            onClick={event => {
                                                                document.forms[
                                                                    "kc-select-try-another-way-form" as never
                                                                ].submit();
                                                                event.preventDefault();
                                                                return false;
                                                            }}
                                                        >
                                                            {msg("doTryAnotherWay")}
                                                        </a>
                                                    </Button>
                                                </div>
                                            </form>
                                        )}
                                    {displayInfo && (
                                        <div className="text-center text-sm">
                                            {infoNode}
                                        </div>
                                    )}
                                </div>
                            </CardContent>
                        </Card>
                    </div>
                </div>
            </div>
        </div>
    );
}
