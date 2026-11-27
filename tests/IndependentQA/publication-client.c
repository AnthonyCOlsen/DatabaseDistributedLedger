/* Standalone native smoke client. Link against the candidate's sqlite3 import
** library. The host owns enrollment. All database operations use ordinary API. */
#include "sqlite3.h"
#include <stdio.h>
#include <string.h>

static int executeBound(sqlite3 *db, const char *sql, const unsigned char *row, const unsigned char *value){
  sqlite3_stmt *s=0;
  int rc=sqlite3_prepare_v2(db,sql,-1,&s,0);
  if( rc==SQLITE_OK && row ) rc=sqlite3_bind_blob(s,1,row,16,SQLITE_STATIC);
  if( rc==SQLITE_OK && value ) rc=sqlite3_bind_blob(s,2,value,2,SQLITE_STATIC);
  if( rc==SQLITE_OK ) rc=sqlite3_step(s);
  printf("SQL [%s] rc=%d message=%s\n",sql,rc,sqlite3_errmsg(db));
  sqlite3_finalize(s);
  return rc;
}

static int execute(sqlite3 *db, const char *sql, const unsigned char *row){
  return executeBound(db,sql,row,0);
}

int main(int argc, char **argv){
  sqlite3 *db=0;
  unsigned char row[16];
  int i, rc, result=6;
  int rollback = argc==4 && strcmp(argv[3],"--rollback")==0;
  int replace = argc==4 && strcmp(argv[3],"--discard-at-savepoint")==0;
  int retain = argc==4 && strcmp(argv[3],"--retain-at-savepoint")==0;
  int outer = argc==4 && strcmp(argv[3],"--outer-release")==0;
  int lost=argc==4 && !strcmp(argv[3],"--lost-reply");
  int update=argc==4 && (!strcmp(argv[3],"--update") || !strcmp(argv[3],"--unchanged") || !strcmp(argv[3],"--update-rollback"));
  int remove=argc==4 && (!strcmp(argv[3],"--delete") || !strcmp(argv[3],"--missing"));
  unsigned char value[2]={0,255};
  if( update && !strcmp(argv[3],"--unchanged") ) value[1]=1;
  if( update && !strcmp(argv[3],"--update-rollback") ) rollback=1;
  if( argc!=3 && !lost && !update && !remove && !rollback && !replace && !retain && !outer ) return 2;
  if( strcmp(argv[1],"--create-seed")==0 ){
    rc=sqlite3_open_v2(argv[2],&db,SQLITE_OPEN_READWRITE|SQLITE_OPEN_CREATE,0);
    if( rc==SQLITE_OK ) rc=sqlite3_exec(db,
      "CREATE TABLE items(row_guid BLOB PRIMARY KEY, value BLOB) WITHOUT ROWID;"
      "INSERT INTO items VALUES(x'01000000000000000000000000000000',x'0001'),(x'02000000000000000000000000000000',x'0102')",0,0,0);
    sqlite3_close(db);
    return rc;
  }
  if( strlen(argv[2])!=32 ) return 2;
  for(i=0;i<16;i++){
    unsigned value;
    if( sscanf(argv[2]+2*i,"%2x",&value)!=1 ) return 2;
    row[i]=(unsigned char)value;
  }
  rc=sqlite3_open_v2(argv[1],&db,SQLITE_OPEN_READWRITE,0);
  if( rc!=SQLITE_OK ){ printf("OPEN rc=%d %s\n",rc,sqlite3_errmsg(db)); goto done; }
  if( execute(db,outer ? "SAVEPOINT staged" : "BEGIN",0)!=SQLITE_DONE ) goto done;
  if( replace || retain ){
    unsigned char first[16];
    memcpy(first,row,sizeof(first));
    if( replace ) first[0]^=0x80;
    if( execute(db,"SAVEPOINT staged",0)!=SQLITE_DONE ||
        execute(db,"INSERT INTO items(row_guid) VALUES(?1)",first)!=SQLITE_DONE ) goto done;
    if( replace && execute(db,"ROLLBACK TO staged",0)!=SQLITE_DONE ) goto done;
    if( execute(db,"RELEASE staged",0)!=SQLITE_DONE || sqlite3_get_autocommit(db) ) goto done;
  }
  if( update ) rc=executeBound(db,"UPDATE items SET value=?2 WHERE row_guid=?1 AND value IS NOT ?2",row,value);
  else if( remove ) rc=execute(db,"DELETE FROM items WHERE row_guid=?1",row);
  else rc=retain ? SQLITE_DONE : execute(db,"INSERT INTO items(row_guid) VALUES(?1)",row);
  if( rc!=SQLITE_DONE ){ if( rc==SQLITE_CONSTRAINT ) result=5; goto done; }
  if( rollback ){
    rc=execute(db,"ROLLBACK",0);
    result=rc==SQLITE_DONE && sqlite3_get_autocommit(db) ? 0 : 6;
    goto done;
  }
  if( replace || retain || outer ){
    puts(outer ? "READY_FOR_OUTER_RELEASE" : "READY_FOR_OUTER_COMMIT");
    fflush(stdout);
    if( getchar()!='G' ) goto done;
  }
  rc=execute(db,outer ? "RELEASE staged" : "COMMIT",0);
  if( lost ){
    if( rc!=SQLITE_IOERR || execute(db,"DELETE FROM items WHERE row_guid=?1",row)!=SQLITE_AUTH ||
        execute(db,"COMMIT",0)!=SQLITE_AUTH ) goto done;
    puts("LOST_REPLY original=IOERR further_dml=AUTH further_commit=AUTH");
  }
  if( outer && (rc==SQLITE_AUTH || rc==SQLITE_IOERR) ){
    /* A fresh RELEASE on this consumed handle must fail without submitting. */
    if( execute(db,"RELEASE staged",0)!=SQLITE_AUTH ) goto done;
  }
  result=rc==SQLITE_DONE ? 0 : rc==SQLITE_AUTH ? 4 : rc==SQLITE_IOERR ? 7 : 6;
done:
  rc=sqlite3_close(db);
  if( rollback ) printf("CLOSE rc=%d\n",rc);
  if( rc!=SQLITE_OK ) return 6;
  return result;
}
