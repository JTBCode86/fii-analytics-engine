#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
tmpdir="$(mktemp -d)"
trap '/bin/rm -f "$tmpdir/aws" "$tmpdir/cp" "$tmpdir/rm" "$tmpdir/calls" "$tmpdir/output"; rmdir "$tmpdir"' EXIT

cat > "$tmpdir/aws" <<'EOF'
#!/usr/bin/env bash
printf '%s\n' "$*" >> "$CALLS"
if [[ "$*" == *"lambda wait function-active-v2 --function-name ${FAIL_FUNCTION:-__none__}"* ]]; then
    exit 255
fi
echo mock
EOF
printf '#!/usr/bin/env bash\nexit 0\n' > "$tmpdir/cp"
printf '#!/usr/bin/env bash\nexit 0\n' > "$tmpdir/rm"
chmod +x "$tmpdir/aws" "$tmpdir/cp" "$tmpdir/rm"
export PATH="$tmpdir:$PATH" CALLS="$tmpdir/calls"

run_init() {
    : > "$CALLS"
    bash "$root/localstack-init/init-aws.sh" > "$tmpdir/output" 2>&1
}

unset FAIL_FUNCTION
run_init
first_wait="$(grep -n 'lambda wait function-active-v2 --function-name processar-carteira-lambda' "$CALLS" | cut -d: -f1)"
second_create="$(grep -n 'lambda create-function --function-name scraper-ativos-lambda' "$CALLS" | cut -d: -f1)"
second_wait="$(grep -n 'lambda wait function-active-v2 --function-name scraper-ativos-lambda' "$CALLS" | cut -d: -f1)"
mapping="$(grep -n 'lambda create-event-source-mapping' "$CALLS" | head -n 1 | cut -d: -f1)"
test "$first_wait" -lt "$second_create"
test "$second_wait" -lt "$mapping"
grep -q 'PROVISIONAMENTO CONCLUÍDO COM SUCESSO' "$tmpdir/output"

for function in processar-carteira-lambda scraper-ativos-lambda; do
    export FAIL_FUNCTION="$function"
    if run_init; then
        echo "O provisionamento deveria falhar para $function." >&2
        exit 1
    fi
    grep -Fq "ERRO: A Lambda ${function} não ficou ativa." "$tmpdir/output"
    if grep -q 'PROVISIONAMENTO CONCLUÍDO COM SUCESSO' "$tmpdir/output" ||
       grep -q 'lambda create-event-source-mapping' "$CALLS"; then
        echo "O provisionamento continuou após a falha de $function." >&2
        exit 1
    fi
    if [[ "$function" == processar-carteira-lambda ]] &&
       grep -q 'lambda create-function --function-name scraper-ativos-lambda' "$CALLS"; then
        echo "A segunda Lambda foi criada após a falha da primeira." >&2
        exit 1
    fi
done

echo "Testes de provisionamento passaram."
