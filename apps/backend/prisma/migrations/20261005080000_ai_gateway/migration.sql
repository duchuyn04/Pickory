-- CreateTable
CREATE TABLE "ai_usage_logs" (
    "id" TEXT NOT NULL,
    "feature" TEXT NOT NULL,
    "provider" TEXT NOT NULL,
    "model" TEXT NOT NULL,
    "promptVersion" TEXT,
    "inputTokens" INTEGER NOT NULL DEFAULT 0,
    "outputTokens" INTEGER NOT NULL DEFAULT 0,
    "cachedTokens" INTEGER NOT NULL DEFAULT 0,
    "costUsd" DOUBLE PRECISION NOT NULL DEFAULT 0,
    "latencyMs" INTEGER NOT NULL,
    "status" TEXT NOT NULL,
    "errorCode" TEXT,
    "actorKeyHash" TEXT,
    "createdAt" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "ai_usage_logs_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "ai_settings" (
    "id" TEXT NOT NULL DEFAULT 'singleton',
    "monthlyBudgetUsd" DOUBLE PRECISION NOT NULL,
    "chatDailyLimitUser" INTEGER NOT NULL,
    "chatDailyLimitAnon" INTEGER NOT NULL,
    "createdAt" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updatedAt" TIMESTAMP(3) NOT NULL,

    CONSTRAINT "ai_settings_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "ai_embedding_cache" (
    "id" TEXT NOT NULL,
    "key" TEXT NOT NULL,
    "model" TEXT NOT NULL,
    "embedding" DOUBLE PRECISION[],
    "createdAt" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "ai_embedding_cache_pkey" PRIMARY KEY ("id")
);

-- CreateIndex
CREATE INDEX "ai_usage_logs_createdAt_idx" ON "ai_usage_logs"("createdAt");

-- CreateIndex
CREATE INDEX "ai_usage_logs_feature_idx" ON "ai_usage_logs"("feature");

-- CreateIndex
CREATE INDEX "ai_usage_logs_status_idx" ON "ai_usage_logs"("status");

-- CreateIndex
CREATE INDEX "ai_usage_logs_actorKeyHash_idx" ON "ai_usage_logs"("actorKeyHash");

-- CreateIndex
CREATE UNIQUE INDEX "ai_embedding_cache_key_key" ON "ai_embedding_cache"("key");

-- CreateIndex
CREATE INDEX "ai_embedding_cache_model_idx" ON "ai_embedding_cache"("model");
