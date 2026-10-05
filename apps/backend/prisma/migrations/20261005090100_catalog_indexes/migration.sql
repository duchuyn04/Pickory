-- CreateIndex
CREATE INDEX "products_minPrice_idx" ON "products"("minPrice");

-- CreateIndex
CREATE INDEX "products_createdAt_idx" ON "products"("createdAt");

-- CreateIndex
CREATE INDEX "skus_productId_idx" ON "skus"("productId");
