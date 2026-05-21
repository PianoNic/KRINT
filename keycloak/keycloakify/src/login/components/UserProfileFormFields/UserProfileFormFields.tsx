import { Field, FieldDescription, FieldLabel } from "@/components/ui/field";
import type { Attribute } from "@keycloakify/login-ui/KcContext";
import type { JSX } from "@keycloakify/login-ui/tools/JSX";
import {
    useUserProfileForm,
    type FormAction,
    type FormFieldError
} from "@keycloakify/login-ui/useUserProfileForm";
import { Fragment, useEffect } from "react";
import { assert } from "tsafe/assert";
import { useKcContext } from "../../KcContext";
import { useI18n } from "../../i18n";
import { DO_MAKE_USER_CONFIRM_PASSWORD } from "./DO_MAKE_USER_CONFIRM_PASSWORD";
import { FieldErrors } from "./FieldErrors";
import { GroupLabel } from "./GroupLabel";
import { InputFieldByType } from "./InputFieldByType";

export type UserProfileFormFieldsProps = {
    onIsFormSubmittableValueChange: (isFormSubmittable: boolean) => void;
    BeforeField?: (props: BeforeAfterFieldProps) => JSX.Element | null;
    AfterField?: (props: BeforeAfterFieldProps) => JSX.Element | null;
};

type BeforeAfterFieldProps = {
    attribute: Attribute;
    dispatchFormAction: React.Dispatch<FormAction>;
    displayableErrors: FormFieldError[];
    valueOrValues: string | string[];
};

export function UserProfileFormFields(props: UserProfileFormFieldsProps) {
    const { onIsFormSubmittableValueChange, BeforeField, AfterField } = props;

    const { kcContext } = useKcContext();

    assert("profile" in kcContext);

    const i18n = useI18n();

    const { advancedMsg } = i18n;

    const {
        formState: { formFieldStates, isFormSubmittable },
        dispatchFormAction
    } = useUserProfileForm({
        kcContext,
        i18n,
        doMakeUserConfirmPassword: DO_MAKE_USER_CONFIRM_PASSWORD
    });

    useEffect(() => {
        onIsFormSubmittableValueChange(isFormSubmittable);
    }, [isFormSubmittable]);

    const groupNameRef = { current: "" };

    const renderFieldInner = (state: typeof formFieldStates[number]) => {
        const { attribute, displayableErrors, valueOrValues } = state;
        return (
            <>
                {BeforeField !== undefined && (
                    <BeforeField
                        attribute={attribute}
                        dispatchFormAction={dispatchFormAction}
                        displayableErrors={displayableErrors}
                        valueOrValues={valueOrValues}
                    />
                )}
                <Field
                    data-invalid={displayableErrors.length > 0 ? "true" : undefined}
                    style={{
                        display:
                            attribute.annotations.inputType === "hidden"
                                ? "none"
                                : undefined
                    }}
                >
                    <FieldLabel htmlFor={attribute.name}>
                        {advancedMsg(attribute.displayName ?? "")}
                        {attribute.required && <> *</>}
                    </FieldLabel>
                    {attribute.annotations.inputHelperTextBefore !== undefined && (
                        <FieldDescription
                            id={`form-help-text-before-${attribute.name}`}
                            aria-live="polite"
                        >
                            {advancedMsg(attribute.annotations.inputHelperTextBefore)}
                        </FieldDescription>
                    )}
                    <InputFieldByType
                        attribute={attribute}
                        valueOrValues={valueOrValues}
                        displayableErrors={displayableErrors}
                        dispatchFormAction={dispatchFormAction}
                    />
                    <FieldErrors
                        attribute={attribute}
                        displayableErrors={displayableErrors}
                        fieldIndex={undefined}
                    />
                    {attribute.annotations.inputHelperTextAfter !== undefined && (
                        <FieldDescription
                            id={`form-help-text-after-${attribute.name}`}
                            aria-live="polite"
                        >
                            {advancedMsg(attribute.annotations.inputHelperTextAfter)}
                        </FieldDescription>
                    )}
                    {AfterField !== undefined && (
                        <AfterField
                            attribute={attribute}
                            dispatchFormAction={dispatchFormAction}
                            displayableErrors={displayableErrors}
                            valueOrValues={valueOrValues}
                        />
                    )}
                </Field>
            </>
        );
    };

    // Reorder: username, firstName, lastName, email, password, password-confirm, then everything else.
    const fieldOrder = [
        "username",
        "firstName",
        "lastName",
        "email",
        "password",
        "password-confirm"
    ];
    const ordered = [...formFieldStates].sort((a, b) => {
        const ai = fieldOrder.indexOf(a.attribute.name);
        const bi = fieldOrder.indexOf(b.attribute.name);
        if (ai === -1 && bi === -1) return 0;
        if (ai === -1) return 1;
        if (bi === -1) return -1;
        return ai - bi;
    });

    // Pair firstName+lastName and password+password-confirm side-by-side.
    const PAIRS: Record<string, string> = {
        firstName: "lastName",
        password: "password-confirm"
    };
    const skip = new Set<string>();
    const nodes: JSX.Element[] = [];

    for (const state of ordered) {
        const { attribute } = state;
        if (skip.has(attribute.name)) continue;

        const partnerName = PAIRS[attribute.name];
        const partner = partnerName
            ? ordered.find(s => s.attribute.name === partnerName)
            : undefined;

        if (partner) {
            skip.add(partnerName);
            nodes.push(
                <Fragment key={attribute.name}>
                    <GroupLabel attribute={attribute} groupNameRef={groupNameRef} />
                    <div className="grid grid-cols-2 gap-3">
                        <div>{renderFieldInner(state)}</div>
                        <div>{renderFieldInner(partner)}</div>
                    </div>
                </Fragment>
            );
        } else {
            nodes.push(
                <Fragment key={attribute.name}>
                    <GroupLabel attribute={attribute} groupNameRef={groupNameRef} />
                    {renderFieldInner(state)}
                </Fragment>
            );
        }
    }

    return <>{nodes}</>;
}
