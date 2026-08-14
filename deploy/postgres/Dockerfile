FROM postgres:16-alpine@sha256:44c4ee9810eff91f7eab4d822642e01115b1a9eccce4bcbdde7604752d68eac6
RUN apk add --no-cache aws-cli bash ca-certificates \
    && rm -f /usr/local/bin/gosu
COPY --chmod=0555 backup.sh restore-verify.sh /usr/local/bin/
USER postgres
ENTRYPOINT []
