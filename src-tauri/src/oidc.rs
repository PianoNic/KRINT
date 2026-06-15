// Minimal embedded OIDC issuer for the desktop app.
//
// Replaces the Docker-based mock-oauth2-server: a tiny in-process authorization server that
// auto-issues tokens (no login screen), so the desktop app has zero auth dependencies. It
// serves exactly what angular-auth-oidc-client (browser) and ASP.NET's JWT bearer (server)
// need:
//   GET  /.well-known/openid-configuration   discovery
//   GET  /jwks                               RSA public key (RS256)
//   GET  /authorize                          immediately redirects back with a code
//   POST /token                              returns RS256-signed access_token + id_token
//   GET  /userinfo, GET /endsession          convenience endpoints
//
// Tokens are signed with a freshly generated RSA-2048 key; the public half is published at
// /jwks so the API can verify signatures via the standard discovery flow.

use std::collections::HashMap;
use std::sync::{Arc, Mutex};
use std::time::{SystemTime, UNIX_EPOCH};

use axum::{
    extract::{Form, Query, State},
    response::{IntoResponse, Json, Redirect},
    routing::{get, post},
    Router,
};
use base64::Engine;
use jsonwebtoken::{encode, Algorithm, EncodingKey, Header};
use rsa::pkcs1::EncodeRsaPrivateKey;
use rsa::traits::PublicKeyParts;
use rsa::{RsaPrivateKey, RsaPublicKey};
use serde::Deserialize;
use serde_json::{json, Value};
use sha2::{Digest, Sha256};
use tower_http::cors::{Any, CorsLayer};

const KID: &str = "krint-local";
const SUBJECT: &str = "krint-local-user";
// Deterministic DiceBear avatar (always the same image) for the single local desktop user.
const AVATAR_URL: &str = "https://api.dicebear.com/10.x/notionists-neutral/svg?seed=Hutch79";

/// Signing material: an RSA key plus its published JWKS components.
struct Keys {
    encoding_key: EncodingKey,
    jwk_n: String,
    jwk_e: String,
}

impl Keys {
    fn generate() -> Result<Self, Box<dyn std::error::Error + Send + Sync>> {
        let mut rng = rand::thread_rng();
        let private = RsaPrivateKey::new(&mut rng, 2048)?;
        let public = RsaPublicKey::from(&private);
        let der = private.to_pkcs1_der()?;
        let b64 = base64::engine::general_purpose::URL_SAFE_NO_PAD;
        Ok(Self {
            encoding_key: EncodingKey::from_rsa_der(der.as_bytes()),
            jwk_n: b64.encode(public.n().to_bytes_be()),
            jwk_e: b64.encode(public.e().to_bytes_be()),
        })
    }

    fn sign(&self, claims: &Value) -> String {
        let mut header = Header::new(Algorithm::RS256);
        header.kid = Some(KID.to_string());
        encode(&header, claims, &self.encoding_key).expect("failed to sign token")
    }
}

#[derive(Clone)]
struct AppState {
    issuer: String,
    client_id: String,
    keys: Arc<Keys>,
    // The local user's identity, derived from the OS account (see local_identity).
    display_name: String,
    username: String,
    // authorization code -> nonce supplied at /authorize, consumed at /token.
    pending: Arc<Mutex<HashMap<String, Option<String>>>>,
}

/// The single local user takes its name from the OS account, so the app greets the actual
/// person instead of a generic "KRINT Local". Returns (display name, username).
fn local_identity() -> (String, String) {
    let username = std::env::var("USERNAME")
        .or_else(|_| std::env::var("USER"))
        .ok()
        .map(|u| u.trim().to_string())
        .filter(|u| !u.is_empty())
        .unwrap_or_else(|| "user".to_string());
    // Turn a login into a human name: split on . _ - separators and title-case each part, so
    // "niclas.erismann" / "NE" -> "Niclas Erismann" / "NE" (no dots in the displayed name).
    let display_name = username
        .split(|c| c == '.' || c == '_' || c == '-')
        .filter(|p| !p.is_empty())
        .map(|p| {
            let mut ch = p.chars();
            match ch.next() {
                Some(first) => first.to_uppercase().collect::<String>() + ch.as_str(),
                None => String::new(),
            }
        })
        .collect::<Vec<_>>()
        .join(" ");
    let display_name = if display_name.is_empty() { username.clone() } else { display_name };
    (display_name, username)
}

/// Start the issuer on 127.0.0.1:`port` with `issuer` as the public URL. Runs until the
/// process exits.
pub async fn serve(
    issuer: String,
    client_id: String,
    port: u16,
) -> Result<(), Box<dyn std::error::Error + Send + Sync>> {
    let (display_name, username) = local_identity();
    let state = AppState {
        issuer,
        client_id,
        keys: Arc::new(Keys::generate()?),
        display_name,
        username,
        pending: Arc::new(Mutex::new(HashMap::new())),
    };

    let app = Router::new()
        .route("/.well-known/openid-configuration", get(discovery))
        .route("/jwks", get(jwks))
        .route("/authorize", get(authorize))
        .route("/token", post(token))
        .route("/userinfo", get(userinfo))
        .route("/endsession", get(endsession))
        // The browser fetches discovery/jwks/token cross-origin (webview origin -> issuer).
        .layer(
            CorsLayer::new()
                .allow_origin(Any)
                .allow_methods(Any)
                .allow_headers(Any),
        )
        .with_state(state);

    let listener = tokio::net::TcpListener::bind(("127.0.0.1", port)).await?;
    axum::serve(listener, app).await?;
    Ok(())
}

async fn discovery(State(s): State<AppState>) -> Json<Value> {
    let iss = &s.issuer;
    Json(json!({
        "issuer": iss,
        "authorization_endpoint": format!("{iss}/authorize"),
        "token_endpoint": format!("{iss}/token"),
        "jwks_uri": format!("{iss}/jwks"),
        "userinfo_endpoint": format!("{iss}/userinfo"),
        "end_session_endpoint": format!("{iss}/endsession"),
        "response_types_supported": ["code"],
        "grant_types_supported": ["authorization_code"],
        "subject_types_supported": ["public"],
        "id_token_signing_alg_values_supported": ["RS256"],
        "scopes_supported": ["openid", "profile", "email", "roles"],
        "token_endpoint_auth_methods_supported": ["none", "client_secret_post"],
        "code_challenge_methods_supported": ["S256", "plain"]
    }))
}

async fn jwks(State(s): State<AppState>) -> Json<Value> {
    Json(json!({
        "keys": [{
            "kty": "RSA",
            "use": "sig",
            "alg": "RS256",
            "kid": KID,
            "n": s.keys.jwk_n,
            "e": s.keys.jwk_e
        }]
    }))
}

#[derive(Deserialize)]
struct AuthorizeParams {
    redirect_uri: String,
    state: Option<String>,
    nonce: Option<String>,
}

/// No interactive login: mint a code immediately and bounce back to the SPA.
async fn authorize(State(s): State<AppState>, Query(p): Query<AuthorizeParams>) -> Redirect {
    let code = random_token(32);
    s.pending.lock().unwrap().insert(code.clone(), p.nonce);

    let sep = if p.redirect_uri.contains('?') { '&' } else { '?' };
    let mut url = format!("{}{}code={}", p.redirect_uri, sep, code);
    if let Some(state) = p.state {
        url.push_str(&format!("&state={state}"));
    }
    Redirect::to(&url)
}

#[derive(Deserialize)]
struct TokenParams {
    code: Option<String>,
}

async fn token(State(s): State<AppState>, Form(p): Form<TokenParams>) -> Json<Value> {
    let nonce = p
        .code
        .as_ref()
        .and_then(|c| s.pending.lock().unwrap().remove(c))
        .flatten();

    let now = unix_now();
    let exp = now + 3600;

    let access = s.keys.sign(&claims(&s, now, exp, None, None));
    let id = s.keys.sign(&claims(&s, now, exp, nonce, Some(at_hash(&access))));

    Json(json!({
        "access_token": access,
        "id_token": id,
        "token_type": "Bearer",
        "expires_in": 3600,
        "scope": "openid profile email roles"
    }))
}

async fn userinfo(State(s): State<AppState>) -> Json<Value> {
    Json(user_claims(&s))
}

#[derive(Deserialize)]
struct EndSessionParams {
    post_logout_redirect_uri: Option<String>,
}

async fn endsession(Query(p): Query<EndSessionParams>) -> impl IntoResponse {
    match p.post_logout_redirect_uri {
        Some(uri) => Redirect::to(&uri).into_response(),
        None => axum::http::StatusCode::OK.into_response(),
    }
}

/// The single local user. Mirrors the e2e mock's admin identity.
fn user_claims(s: &AppState) -> Value {
    json!({
        "sub": SUBJECT,
        "name": s.display_name,
        // The SPA shows preferred_username as the display name, so use the prettified form too
        // (the raw login lives on in the email below).
        "preferred_username": s.display_name,
        "email": format!("{}@localhost", s.username.to_lowercase()),
        "email_verified": true,
        "picture": AVATAR_URL,
        "roles": ["admin"],
        "aud": s.client_id,
        "iss": s.issuer
    })
}

fn claims(s: &AppState, iat: u64, exp: u64, nonce: Option<String>, at_hash: Option<String>) -> Value {
    let mut m = user_claims(s);
    m["iat"] = json!(iat);
    m["nbf"] = json!(iat);
    m["auth_time"] = json!(iat);
    m["exp"] = json!(exp);
    if let Some(n) = nonce {
        m["nonce"] = json!(n);
    }
    if let Some(h) = at_hash {
        m["at_hash"] = json!(h);
    }
    m
}

/// at_hash = base64url(left-most half of SHA-256(access_token)).
fn at_hash(access_token: &str) -> String {
    let digest = Sha256::digest(access_token.as_bytes());
    base64::engine::general_purpose::URL_SAFE_NO_PAD.encode(&digest[..16])
}

fn unix_now() -> u64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap()
        .as_secs()
}

fn random_token(len: usize) -> String {
    use rand::Rng;
    const CHARS: &[u8] = b"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    let mut rng = rand::thread_rng();
    (0..len)
        .map(|_| CHARS[rng.gen_range(0..CHARS.len())] as char)
        .collect()
}

#[cfg(test)]
mod tests {
    use super::*;
    use jsonwebtoken::{decode, DecodingKey, Validation};

    // Sign a token with the private key, then verify it with the public components we publish
    // at /jwks - the exact crypto path the ASP.NET API uses to validate access tokens.
    #[test]
    fn signed_token_verifies_against_published_jwks() {
        let keys = Keys::generate().unwrap();
        let state = AppState {
            issuer: "http://localhost:18080".into(),
            client_id: "krint".into(),
            keys: Arc::new(keys),
            display_name: "Test User".into(),
            username: "test".into(),
            pending: Arc::new(Mutex::new(HashMap::new())),
        };

        let now = unix_now();
        let token = state.keys.sign(&claims(&state, now, now + 3600, None, None));

        let decoding = DecodingKey::from_rsa_components(&state.keys.jwk_n, &state.keys.jwk_e).unwrap();
        let mut validation = Validation::new(Algorithm::RS256);
        validation.validate_aud = false;
        validation.set_issuer(&["http://localhost:18080"]);

        let decoded = decode::<Value>(&token, &decoding, &validation).expect("token must verify");
        assert_eq!(decoded.claims["roles"][0], "admin");
        assert_eq!(decoded.claims["iss"], "http://localhost:18080");
    }
}
