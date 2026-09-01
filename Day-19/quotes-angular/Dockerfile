FROM node:20-alpine

WORKDIR /app

COPY server/package.json server/package-lock.json* ./
# node_modules installed on the host and copied in rather than `npm install`
# here - this sandbox's docker build containers have no outbound network
# access (only the daemon's own image pulls do), so an in-build npm install
# fails with EHOSTUNREACH.
COPY server/node_modules ./node_modules

COPY server/server.js ./
COPY dist/quotes-angular/browser ./public

ENV PORT=8080
EXPOSE 8080

CMD ["node", "server.js"]
