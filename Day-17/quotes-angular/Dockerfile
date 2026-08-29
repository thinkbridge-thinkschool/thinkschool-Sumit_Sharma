FROM node:20-alpine

WORKDIR /app

COPY server/package.json server/package-lock.json* ./
RUN npm install --omit=dev

COPY server/server.js ./
COPY dist/quotes-angular/browser ./public

ENV PORT=8080
EXPOSE 8080

CMD ["node", "server.js"]
