import {ErrorHandler, Injectable, signal} from '@angular/core';
export {api, request, errorMessage} from './api';

@Injectable({providedIn:'root'})
export class AppErrors implements ErrorHandler {
  readonly message=signal('');
  handleError(error:unknown):void {
    console.error('Application error', error);
    this.message.set('An unexpected application error occurred. Refresh the page if the problem continues.');
  }
}
